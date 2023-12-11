using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace MultiFunPlayer.Common;

internal static class ConnectableExtensions
{
    public static Task WaitForIdle(this IConnectable connectable, CancellationToken token)
        => connectable.WaitForStatus(new[] { ConnectionStatus.Connected, ConnectionStatus.Disconnected }, token);
    public static Task WaitForDisconnect(this IConnectable connectable, CancellationToken token)
        => connectable.WaitForStatus(new[] { ConnectionStatus.Disconnected }, token);
}

public static class JsonExtensions
{
    public static bool TryToObject<T>(this JToken token, out T value) => TryToObject(token, JsonSerializer.CreateDefault(), out value);
    public static bool TryToObject<T>(this JToken token, JsonSerializerSettings settings, out T value) => TryToObject(token, JsonSerializer.CreateDefault(settings), out value);
    public static bool TryToObject<T>(this JToken token, JsonSerializer serializer, out T value)
    {
        value = default;

        try
        {
            if (token.Type == JTokenType.Null)
                return false;

            value = token.ToObject<T>(serializer);
            return value != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetValue<T>(this JObject o, string propertyName, out T value) => TryGetValue(o, propertyName, JsonSerializer.CreateDefault(), out value);
    public static bool TryGetValue<T>(this JObject o, string propertyName, JsonSerializerSettings settings, out T value) => TryGetValue(o, propertyName, JsonSerializer.CreateDefault(settings), out value);
    public static bool TryGetValue<T>(this JObject o, string propertyName, JsonSerializer serializer, out T value)
    {
        value = default;
        return o.TryGetValue(propertyName, out var token) && token.TryToObject(serializer, out value);
    }

    public static bool EnsureContainsObjects(this JToken token, params string[] propertyNames)
    {
        if (token is not JObject o)
            return false;

        foreach (var propertyName in propertyNames)
        {
            if (!o.ContainsKey(propertyName))
                o[propertyName] = new JObject();

            if (o[propertyName] is JObject child)
                o = child;
            else
                return false;
        }

        return true;
    }

    public static bool TryGetObject(this JToken token, out JObject result, params string[] propertyNames)
    {
        result = null;
        if (token is not JObject o)
            return false;

        foreach (var propertyName in propertyNames)
        {
            if (!o.ContainsKey(propertyName) || o[propertyName] is not JObject child)
                return false;

            o = child;
        }

        result = o;
        return true;
    }

    public static void Populate(this JToken token, object target) => Populate(token, target, JsonSerializer.CreateDefault());
    public static void Populate(this JToken token, object target, JsonSerializerSettings settings) => Populate(token, target, JsonSerializer.CreateDefault(settings));
    public static void Populate(this JToken token, object target, JsonSerializer serializer)
    {
        using var reader = token.CreateReader();
        serializer.Populate(reader, target);
    }

    public static void AddTypeProperty(this JObject o, Type type)
        => o.Add("$type", ReflectionUtils.RemoveAssemblyDetails(type.AssemblyQualifiedName));

    public static Type GetTypeProperty(this JObject o)
    {
        var (_, valueTypeName) = ReflectionUtils.SplitFullyQualifiedTypeName(o["$type"].ToString());
        return Type.GetType(valueTypeName);
    }

    public static bool RenameProperty(this JObject o, string oldName, string newName)
    {
        if (string.Equals(oldName, newName))
            return true;

        if (!o.ContainsKey(oldName))
            return false;

        var p = o.Property(oldName);
        p.Replace(new JProperty(newName, p.Value));

        return true;
    }
}

public static class TaskExtensions
{
    public static Task WithCancellation(this Task task, CancellationToken cancellationToken)
    {
        static async Task DoWaitAsync(Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource();
            await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
            await await Task.WhenAny(task, tcs.Task);
        }

        if (!cancellationToken.CanBeCanceled)
            return task;
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        return DoWaitAsync(task, cancellationToken);
    }

    public static Task<TResult> WithCancellation<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
    {
        static async Task<T> DoWaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<T>();
            await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
            return await await Task.WhenAny(task, tcs.Task);
        }

        if (!cancellationToken.CanBeCanceled)
            return task;
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TResult>(cancellationToken);
        return DoWaitAsync(task, cancellationToken);
    }

    public static Task WithCancellation(this Task task, int millisecondsDelay)
    {
        static async Task DoWaitAsync(Task task, int millisecondsDelay)
        {
            var tcs = new TaskCompletionSource();
            using var cancellationSource = new CancellationTokenSource(millisecondsDelay);
            await using var registration = cancellationSource.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            try { await await Task.WhenAny(task, tcs.Task); }
            catch (OperationCanceledException) when (tcs.Task.IsCanceled) { throw new TimeoutException(); }
        }

        return DoWaitAsync(task, millisecondsDelay);
    }

    public static Task<TResult> WithCancellation<TResult>(this Task<TResult> task, int millisecondsDelay)
    {
        static async Task<T> DoWaitAsync<T>(Task<T> task, int millisecondsDelay)
        {
            var tcs = new TaskCompletionSource<T>();
            using var cancellationSource = new CancellationTokenSource(millisecondsDelay);
            await using var registration = cancellationSource.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            try { return await await Task.WhenAny(task, tcs.Task); }
            catch (OperationCanceledException) when (tcs.Task.IsCanceled) { throw new TimeoutException(); }
        }

        return DoWaitAsync(task, millisecondsDelay);
    }

    public static void ThrowIfFaulted(this Task task)
    {
        var e = task.Exception;
        if (e == null)
            return;

        if (e.InnerExceptions.Count == 1)
            e.InnerExceptions[0].Throw();
        else
            e.Throw();
    }
}

public static class IOExtensions
{
    public static T AsRefreshed<T>(this T info) where T : FileSystemInfo
    {
        info.Refresh();
        return info;
    }

    private static IEnumerable<T> GuardEnumerate<T>(DirectoryInfo directory, Func<DirectoryInfo, IEnumerable<T>> action) where T : FileSystemInfo
    {
        try
        {
            directory.Refresh();
            if (directory.Exists)
                return action.Invoke(directory);
        } catch { }

        return Enumerable.Empty<T>();
    }

    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory) => directory.SafeEnumerateDirectories("*");
    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory, string searchPattern) => directory.SafeEnumerateDirectories(searchPattern, IOUtils.CreateEnumerationOptions());
    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory, string searchPattern, EnumerationOptions enumerationOptions) => GuardEnumerate(directory, d => d.EnumerateDirectories(searchPattern, enumerationOptions));

    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory) => directory.SafeEnumerateFiles("*.*");
    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory, string searchPattern) => directory.SafeEnumerateFiles(searchPattern, IOUtils.CreateEnumerationOptions());
    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory, string searchPattern, EnumerationOptions enumerationOptions) => GuardEnumerate(directory, d => d.EnumerateFiles(searchPattern, enumerationOptions));

    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory) => directory.SafeEnumerateFileSystemInfos("*.*");
    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory, string searchPattern) => directory.SafeEnumerateFileSystemInfos(searchPattern, IOUtils.CreateEnumerationOptions());
    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory, string searchPattern, EnumerationOptions enumerationOptions) => GuardEnumerate(directory, d => d.EnumerateFileSystemInfos(searchPattern, enumerationOptions));
}

public static class CollectionExtensions
{
    public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
        this IReadOnlyObservableConcurrentDictionary<TKey, TValue> dictionary, Expression<Func<TValue, TView>> selector) where TValue : class
        => new(dictionary, selector);

    public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
        this IReadOnlyObservableConcurrentDictionary<TKey, TValue> dictionary, Func<TValue, TView> selector, string propertyName) where TValue : class
        => new(dictionary, selector, propertyName);

    public static ObservableConcurrentCollectionView<TValue, TView> CreateView<TValue, TView>(
        this IReadOnlyObservableConcurrentCollection<TValue> collection, Expression<Func<TValue, TView>> selector) where TValue : class
        => new(collection, selector);

    public static ObservableConcurrentCollectionView<TValue, TView> CreateView<TValue, TView>(
        this IReadOnlyObservableConcurrentCollection<TValue> collection, Func<TValue, TView> selector, string propertyName) where TValue : class
        => new(collection, selector, propertyName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ValidateIndex<T>(this ICollection<T> collection, int index)
        => index >= 0 && index < collection.Count;

    public static bool ValidateIndex<T>(this IReadOnlyCollection<T> collection, int index)
        => index >= 0 && index < collection.Count;

    public static bool TryGet<T>(this IList list, int index, out T value)
    {
        value = default;
        if (index < 0 || index >= list.Count)
            return false;

        var o = list[index];
        if (o == null)
            return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;

        if (o is not T)
            return false;

        value = (T)o;
        return true;
    }

    public static bool TryGet<T>(this IReadOnlyList<T> list, int index, out T value)
    {
        value = default;
        if (!list.ValidateIndex(index))
            return false;

        value = list[index];
        return true;
    }

    public static IEnumerable<T> NotNull<T>(this IEnumerable<T> enumerable) where T : class => enumerable.Where(x => x != null);

    public static void Merge<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        foreach (var (key, value) in other)
            dictionary[key] = value;
    }
}

public static class StreamExtensions
{
    public static async Task<byte[]> ReadBytesAsync(this NetworkStream stream, int count, CancellationToken token)
    {
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(1024);
        await using var memoryStream = new MemoryStream();

        var readMemory = memoryOwner.Memory;
        while (memoryStream.Position < count)
        {
            var remaining = Math.Min(count - (int)memoryStream.Position, readMemory.Length);
            var read = await stream.ReadAsync(readMemory[..remaining], token);
            if (read == 0)
                break;

            await memoryStream.WriteAsync(readMemory[..read], token);
        }

        return memoryStream.ToArray();
    }

    public static byte[] ReadBytes(this NetworkStream stream, int count)
    {
        using var memoryStream = new MemoryStream();
        var readBuffer = ArrayPool<byte>.Shared.Rent(1024);

        while (memoryStream.Position < count)
        {
            var remaining = Math.Min(count - (int)memoryStream.Position, readBuffer.Length);
            var read = stream.Read(readBuffer.AsSpan(0, remaining));
            if (read == 0)
                break;

            memoryStream.Write(readBuffer.AsSpan(0, read));
        }

        return memoryStream.ToArray();
    }
}

public static class WebSocketExtensions
{
    public static async Task<byte[]> ReceiveAsync(this ClientWebSocket client, CancellationToken token)
    {
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(1024);
        await using var memoryStream = new MemoryStream();

        var readMemory = memoryOwner.Memory;
        var result = default(ValueWebSocketReceiveResult);
        do
        {
            result = await client.ReceiveAsync(readMemory, token);
            await memoryStream.WriteAsync(readMemory[..result.Count], token);
        } while (!token.IsCancellationRequested && !result.EndOfMessage);

        return memoryStream.ToArray();
    }
}

public static class DeconstructExtensions
{
    public static void Deconstruct<TKey, TItems>(this IGrouping<TKey, TItems> grouping, out TKey key, out IEnumerable<TItems> items)
    {
        key = grouping.Key;
        items = grouping.AsEnumerable();
    }
}

public static class NetExtensions
{
    public static async Task DownloadFileAsync(this HttpClient client, Uri address, string fileName)
    {
        using var response = await client.GetAsync(address);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(fileName);
        await stream.CopyToAsync(fileStream);
    }

    public static ValueTask ConnectAsync(this TcpClient client , EndPoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint is IPEndPoint ipEndPoint)
            return client.ConnectAsync(ipEndPoint.Address, ipEndPoint.Port, cancellationToken);
        if (endpoint is DnsEndPoint dnsEndPoint)
            return client.ConnectAsync(dnsEndPoint.Host, dnsEndPoint.Port, cancellationToken);

        throw new NotSupportedException($"{endpoint.GetType()} in not supported.");
    }

    public static void Connect(this TcpClient client, EndPoint endpoint)
    {
        if (endpoint is IPEndPoint ipEndPoint)
            client.Connect(ipEndPoint.Address, ipEndPoint.Port);
        else if (endpoint is DnsEndPoint dnsEndPoint)
            client.Connect(dnsEndPoint.Host, dnsEndPoint.Port);
        else
            throw new NotSupportedException($"{endpoint.GetType()} in not supported.");
    }

    public static void Connect(this UdpClient client, EndPoint endpoint)
    {
        if (endpoint is IPEndPoint ipEndPoint)
            client.Connect(ipEndPoint.Address, ipEndPoint.Port);
        else if (endpoint is DnsEndPoint dnsEndPoint)
            client.Connect(dnsEndPoint.Host, dnsEndPoint.Port);
        else
            throw new NotSupportedException($"{endpoint.GetType()} in not supported.");
    }

    public static bool IsLocalhost(this EndPoint endpoint)
    {
        if (endpoint is IPEndPoint ipEndPoint)
            return IPAddress.IsLoopback(ipEndPoint.Address);
        if (endpoint is DnsEndPoint dnsEndPoint)
            return string.Equals(dnsEndPoint.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        throw new NotSupportedException($"{endpoint.GetType()} in not supported.");
    }

    public static string ToUriString(this EndPoint endpoint)
    {
        if (endpoint is IPEndPoint ipEndPoint)
            return $"{ipEndPoint.Address}:{ipEndPoint.Port}";
        if (endpoint is DnsEndPoint dnsEndPoint)
            return $"{dnsEndPoint.Host}:{dnsEndPoint.Port}";

        throw new NotSupportedException($"{endpoint.GetType()} in not supported.");
    }
}

public static class ExceptionExtensions
{
    public static void Throw(this Exception e) => ExceptionDispatchInfo.Capture(e).Throw();
}

public static class StopwatchExtensions
{
    public static void SleepPrecise(this Stopwatch stopwatch, double millisecondsTimeout)
    {
        if (millisecondsTimeout < 0)
            return;

        var spinner = new SpinWait();
        var frequencyInverse = 1d / Stopwatch.Frequency;
        while (true)
        {
            var elapsed = stopwatch.ElapsedTicks * frequencyInverse * 1000;
            var diff = millisecondsTimeout - elapsed;
            if (diff <= 0)
                break;

            if (diff <= 2) spinner.SpinOnce(-1);
            else if (diff < 5) Thread.Sleep(1);
            else if (diff < 15) Thread.Sleep(5);
            else Thread.Sleep(10);
        }
    }
}