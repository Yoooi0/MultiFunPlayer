using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MultiFunPlayer.Common
{
    public static class Extensions
    {
        public static Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            return task.IsCompleted
                ? task
                : task.ContinueWith(
                    completedTask => completedTask.GetAwaiter().GetResult(),
                    cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ValidateIndex<T>(this ICollection<T> collection, int index)
            => index >= 0 && index < collection.Count;

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
    }
}
