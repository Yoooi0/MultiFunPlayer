using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common
{
    public static class Extensions
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
            catch(FormatException)
            {
                return false;
            }
        }

        public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
            this ObservableConcurrentDictionary<TKey, TValue> dictionary, Expression<Func<TValue, TView>> selector) where TValue : class
            => new(dictionary, selector);

        public static ObservableConcurrentDictionaryView<TKey, TValue, TView> CreateView<TKey, TValue, TView>(
            this ObservableConcurrentDictionary<TKey, TValue> dictionary, Func<TValue, TView> selector, string propertyName) where TValue : class
            => new(dictionary, selector, propertyName);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ValidateIndex<T>(this ICollection<T> collection, int index)
            => index >= 0 && index < collection.Count;

        public static bool TryGet<T>(this IList list, int index, out T value)
        {
            value = default;
            if (index < 0 || index >= list.Count)
                return false;

            var o = list[index];
            if(o == null)
                return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;

            if (o is not T)
                return false;

            value = (T) o;
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

            foreach(var propertyName in propertyNames)
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

        public static void PreciseSleep(this Stopwatch stopwatch, float interval, CancellationToken token)
        {
            var nextTrigger = stopwatch.ElapsedMilliseconds + interval;
            if (stopwatch.Elapsed.TotalHours >= 1d)
            {
                stopwatch.Restart();
                nextTrigger = 0f;
            }

            var tickLength = 1000f / Stopwatch.Frequency;
            while (!token.IsCancellationRequested)
            {
                var elapsed = stopwatch.ElapsedTicks * tickLength;
                var diff = nextTrigger - elapsed;
                if (diff <= 0f)
                    break;

                if (diff < 1f)
                    Thread.SpinWait(10);
                else if (diff < 5f)
                    Thread.SpinWait(100);
                else if (diff < 15f)
                    Thread.Sleep(1);
                else
                    Thread.Sleep(10);
            }
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
