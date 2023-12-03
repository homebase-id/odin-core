using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Odin.Core.Util
{
    public static class Utils
    {
        public static void StopWatchStatus(string s, Stopwatch stopWatch)
        {
            TimeSpan ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);

            Console.WriteLine(elapsedTime + " : " + s);
            stopWatch.Reset();
        }

        // Swaps i1 with i2 and returns i2
        public static int swap(ref int i1, ref int i2)
        {
            int t = i1;
        
            i1 = i2;
            i2 = t;

            return i2;
        }
        
        public static void DummyTypes(List<Guid> dummyArray, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dummyArray.Add(SequentialGuid.CreateGuid());
            }
        }
    }
}   