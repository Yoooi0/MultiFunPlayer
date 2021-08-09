using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common
{
    public static class ConnectableExtensions
    {
        public static Task WaitForIdle(this IConnectable connectable, CancellationToken token)
            => connectable.WaitForStatus(new[] { ConnectionStatus.Connected, ConnectionStatus.Disconnected }, token);
        public static Task WaitForDisconnect(this IConnectable connectable, CancellationToken token)
            => connectable.WaitForStatus(new[] { ConnectionStatus.Disconnected }, token);
    }

    public static class JsonExtensions
    {
        public static bool TryToObject<T>(this JToken token, out T value)
        {
            value = default;

            try
            {
                if (token.Type == JTokenType.Null)
                    return false;

                value = token.ToObject<T>();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryGetValue<T>(this JObject o, string propertyName, out T value)
        {
            value = default;
            return o.TryGetValue(propertyName, out var token) && token.TryToObject(out value);
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

        public static void Populate(this JToken token, object target)
        {
            using var reader = token.CreateReader();
            JsonSerializer.CreateDefault().Populate(reader, target);
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
    }

    public static class IOExtensions
    {
        public static T AsRefreshed<T>(this T info) where T : FileSystemInfo
        {
            info.Refresh();
            return info;
        }
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
        public static async Task<byte[]> ReadAllBytesAsync(this NetworkStream stream, CancellationToken token)
        {
            var result = 0;
            var buffer = new ArraySegment<byte>(new byte[1024]);
            using var memory = new MemoryStream();
            do
            {
                result = await stream.ReadAsync(buffer, token);
                await memory.WriteAsync(buffer.AsMemory(buffer.Offset, result), token);
            }
            while (result > 0 && stream.DataAvailable);

            memory.Seek(0, SeekOrigin.Begin);
            return memory.ToArray();
        }
    }

    public static class EnumUtils
    {
        public static T[] GetValues<T>() where T : Enum
            => (T[])Enum.GetValues(typeof(T));

        public static Dictionary<TEnum, TValue> ToDictionary<TEnum, TValue>(Func<TEnum, TValue> valueGenerator) where TEnum : Enum
            => GetValues<TEnum>().ToDictionary(x => x, valueGenerator);
    }
}
