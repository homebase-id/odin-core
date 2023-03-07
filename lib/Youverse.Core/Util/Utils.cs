using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Youverse.Core.Util
{
    public static class Utils
    {
        // Swaps i1 with i2 and returns i2
        public static int swap(ref int i1, ref int i2)
        {
            int t = i1;
        
            i1 = i2;
            i2 = t;

            return i2;
        }


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

        public static void DummyTypes(List<Guid> dummyArray, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dummyArray.Add(SequentialGuid.CreateGuid());
            }
        }
        
        public static void ShellExecute(string cmd)
        {
            var escapedArgs = cmd.Replace("\"", "\\\"");
        
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };

            process.Start();
            process.WaitForExit();
        }
    }
}   