using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.IO;
using System.Linq.Expressions;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace MultiFunPlayer.Common;

public static class ConnectableExtensions
{
    public static Task WaitForIdle(this IConnectable connectable, CancellationToken token)
        => connectable.WaitForStatus(new[] { ConnectionStatus.Connected, ConnectionStatus.Disconnected }, token);
    public static Task WaitForDisconnect(this IConnectable connectable, CancellationToken token)
        => connectable.WaitForStatus(new[] { ConnectionStatus.Disconnected }, token);

    public static Task WaitForIdle(this IConnectable connectable) => connectable.WaitForIdle(CancellationToken.None);
    public static Task WaitForDisconnect(this IConnectable connectable) => connectable.WaitForDisconnect(CancellationToken.None);
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
}

public static class TaskExtensions
{
    public static Task WithCancellation(this Task task, CancellationToken cancellationToken)
    {
        static async Task DoWaitAsync(Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource();
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
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
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken), useSynchronizationContext: false);
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
            using var registration = cancellationSource.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

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
            using var registration = cancellationSource.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            try { return await await Task.WhenAny(task, tcs.Task); }
            catch (OperationCanceledException) when (tcs.Task.IsCanceled) { throw new TimeoutException(); }
        }

        return DoWaitAsync(task, millisecondsDelay);
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

    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory) => GuardEnumerate(directory, d => d.EnumerateDirectories());
    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory, string searchPattern) => GuardEnumerate(directory, d => d.EnumerateDirectories(searchPattern));
    public static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(this DirectoryInfo directory, string searchPattern, SearchOption searchOption) => GuardEnumerate(directory, d => d.EnumerateDirectories(searchPattern, searchOption));

    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory) => GuardEnumerate(directory, d => d.EnumerateFiles());
    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory, string searchPattern) => GuardEnumerate(directory, d => d.EnumerateFiles(searchPattern));
    public static IEnumerable<FileInfo> SafeEnumerateFiles(this DirectoryInfo directory, string searchPattern, SearchOption searchOption) => GuardEnumerate(directory, d => d.EnumerateFiles(searchPattern, searchOption));

    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory) => GuardEnumerate(directory, d => d.EnumerateFileSystemInfos());
    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory, string searchPattern) => GuardEnumerate(directory, d => d.EnumerateFileSystemInfos(searchPattern));
    public static IEnumerable<FileSystemInfo> SafeEnumerateFileSystemInfos(this DirectoryInfo directory, string searchPattern, SearchOption searchOption) => GuardEnumerate(directory, d => d.EnumerateFileSystemInfos(searchPattern, searchOption));
}

public static class CollectionExtensions
{
    public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
        this ObservableConcurrentDictionary<TKey, TValue> dictionary, Expression<Func<TValue, TView>> selector) where TValue : class
        => new(dictionary, selector);

    public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
        this ObservableConcurrentDictionary<TKey, TValue> dictionary, Func<TValue, TView> selector, string propertyName) where TValue : class
        => new(dictionary, selector, propertyName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ValidateIndex<T>(this ICollection<T> collection, int index)
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

    public static bool TryGet<T>(this IList<T> list, int index, out T value)
    {
        value = default;
        if (!list.ValidateIndex(index))
            return false;

        value = list[index];
        return true;
    }
}

public static class StreamExtensions
{
    private static readonly byte[] _readBuffer = new byte[1024];

    public static async Task<byte[]> ReadBytesAsync(this NetworkStream stream, int count, CancellationToken token)
    {
        using var memory = new MemoryStream();

        while(memory.Position < count)
        {
            var read = await stream.ReadAsync(_readBuffer.AsMemory(0, Math.Min(_readBuffer.Length, count)), token);
            if (read == 0)
                break;

            await memory.WriteAsync(_readBuffer.AsMemory(0, read), token);
        }

        memory.Seek(0, SeekOrigin.Begin);
        return memory.ToArray();
    }

    public static byte[] ReadBytes(this NetworkStream stream, int count)
    {
        using var memory = new MemoryStream();

        while (memory.Position < count)
        {
            var read = stream.Read(_readBuffer, 0, Math.Min(_readBuffer.Length, count));
            if (read == 0)
                break;

            memory.Write(_readBuffer, 0, read);
        }

        memory.Seek(0, SeekOrigin.Begin);
        return memory.ToArray();
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

public static class WebExtensions
{
    public static async Task DownloadFileAsync(this HttpClient client, Uri address, string fileName)
    {
        using var response = await client.GetAsync(address);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(fileName);
        stream.CopyTo(fileStream);
    }
}