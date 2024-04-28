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
        => connectable.WaitForStatus([ConnectionStatus.Connected, ConnectionStatus.Disconnected], token);
    public static Task WaitForDisconnect(this IConnectable connectable, CancellationToken token)
        => connectable.WaitForStatus([ConnectionStatus.Disconnected], token);
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
        var (assemblyName, valueTypeName) = ReflectionUtils.SplitFullyQualifiedTypeName(o["$type"].ToString());
        if (assemblyName == null)
            return Type.GetType(valueTypeName);
        return Type.GetType($"{valueTypeName}, {assemblyName}");
    }
}

public static class TaskExtensions
{
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

        return [];
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
    public static async Task<byte[]> ReadExactlyAsync(this Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, token);
        return buffer;
    }

    public static byte[] ReadExactly(this Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
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

    public static IPAddress[] GetAddresses(this EndPoint endpoint)
    {
        try
        {
            if (endpoint is IPEndPoint ipEndPoint)
                return [ipEndPoint.Address];
            if (endpoint is DnsEndPoint dnsEndPoint)
                return Dns.GetHostAddresses(dnsEndPoint.Host, dnsEndPoint.AddressFamily);
        }
        catch { }

        return [];
    }

    public static async ValueTask<IPAddress[]> GetAddressesAsync(this EndPoint endpoint)
    {
        try
        {
            if (endpoint is IPEndPoint ipEndPoint)
                return [ipEndPoint.Address];
            if (endpoint is DnsEndPoint dnsEndPoint)
                return await Dns.GetHostAddressesAsync(dnsEndPoint.Host, dnsEndPoint.AddressFamily);
        }
        catch { }

        return [];
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

public static class WaitHandleExtensions
{
    public static bool WaitOne(this WaitHandle handle, CancellationToken cancellationToken)
        => ThrowIfCancellationRequested(WaitHandle.WaitAny([handle, cancellationToken.WaitHandle]) == 0, cancellationToken);
    public static bool WaitOne(this WaitHandle handle, int millisecondsTimeout, CancellationToken cancellationToken)
        => ThrowIfCancellationRequested(WaitHandle.WaitAny([handle, cancellationToken.WaitHandle], millisecondsTimeout) == 0, cancellationToken);
    public static bool WaitOne(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
        => ThrowIfCancellationRequested(WaitHandle.WaitAny([handle, cancellationToken.WaitHandle], timeout) == 0, cancellationToken);
    public static bool WaitOne(this WaitHandle handle, int millisecondsTimeout, bool exitContext, CancellationToken cancellationToken)
        => ThrowIfCancellationRequested(WaitHandle.WaitAny([handle, cancellationToken.WaitHandle], millisecondsTimeout, exitContext) == 0, cancellationToken);
    public static bool WaitOne(this WaitHandle handle, TimeSpan timeout, bool exitContext, CancellationToken cancellationToken)
        => ThrowIfCancellationRequested(WaitHandle.WaitAny([handle, cancellationToken.WaitHandle], timeout, exitContext) == 0, cancellationToken);

    private static bool ThrowIfCancellationRequested(bool result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public static Task<bool> WaitOneAsync(this WaitHandle handle, CancellationToken cancellationToken)
    {
        if (handle.WaitOne(0))
            return Task.FromResult(true);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        return DoWaitOneAsync(handle, cancellationToken);
    }

    private static async Task<bool> DoWaitOneAsync(WaitHandle handle, CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<bool>();

        using var threadPoolRegistration = new ThreadPoolRegistration(handle, completionSource);
        await using var cancellationTokenRegistration = cancellationToken.Register(CancellationTokenCallback, completionSource, useSynchronizationContext: false);

        return await completionSource.Task;

        static void CancellationTokenCallback(object state)
            => ((TaskCompletionSource<bool>)state).TrySetCanceled(CancellationToken.None);
    }

    private sealed class ThreadPoolRegistration : IDisposable
    {
        private readonly RegisteredWaitHandle _registeredWaitHandle;

        public ThreadPoolRegistration(WaitHandle handle, TaskCompletionSource<bool> completionSource)
        {
            _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(handle, WaitHandleCallback, completionSource, Timeout.InfiniteTimeSpan, executeOnlyOnce: true);

            static void WaitHandleCallback(object state, bool timedOut)
                => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut);
        }

        private void Dispose(bool disposing) => _registeredWaitHandle.Unregister(null);

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}