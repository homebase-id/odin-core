using System;
using System.IO;

// Needs to move to the solution.

namespace Youverse.Core.Storage.SQLite
{
    public class ReindexDrive
    {
        private static void ReindexFiles(DirectoryInfo folder)
        {
            foreach (var file in folder.GetFiles("*.meta"))
            {
                Console.WriteLine("Load meta file and call db.AddEntry()" + file.Name);
                //
                // Here we need to load and parse the .meta file. Then add it to the
                // index via db.AddEntry()
            }
        }

        public static void ReindexTimeSeries()
        {
            try
            {
                DirectoryInfo diRoot = new DirectoryInfo(@"c:\temp\drive\time");

                // Get only subdirectories that contain the letter "p."
                DirectoryInfo[] arrayYear = diRoot.GetDirectories("????");
                Console.WriteLine("The number of directories with 4 letters = {0}.", arrayYear.Length);

                foreach (DirectoryInfo folderYear in arrayYear)
                {
                    Console.WriteLine("The number of files in {0} is {1}", folderYear, folderYear.GetFiles().Length);

                    DirectoryInfo[] arrayMonth = folderYear.GetDirectories("??");
                    foreach (DirectoryInfo folderMonth in arrayMonth)
                    {
                        Console.WriteLine("The number of files in {0} is {1}", folderMonth, folderMonth.GetFiles().Length);

                        DirectoryInfo[] arrayDay = folderMonth.GetDirectories("??");
                        foreach (DirectoryInfo folderDay in arrayDay)
                        {
                            // Now we're ready to index files here.
                            Console.WriteLine("The number of files in {0} is {1}", folderDay, folderDay.GetFiles().Length);
                            ReindexFiles(folderDay);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The process failed: {0}", e.ToString());
            }
        }

        public static void ReindexRandom()
        {
            try
            {
                DirectoryInfo diRoot = new DirectoryInfo(@"c:\temp\drive\random");

                // Get only subdirectories with a single letter
                DirectoryInfo[] arrayFirst = diRoot.GetDirectories("?");
                Console.WriteLine("The number of directories with 4 letters = {0}.", arrayFirst.Length);

                foreach (DirectoryInfo folderFirst in arrayFirst)
                {
                    Console.WriteLine("The number of files in {0} is {1}", folderFirst, folderFirst.GetFiles().Length);

                    DirectoryInfo[] arraySecond = folderFirst.GetDirectories("?");
                    foreach (DirectoryInfo folderSecond in arraySecond)
                    {
                        Console.WriteLine("The number of files in {0} is {1}", folderSecond, folderSecond.GetFiles().Length);
                        // Now we're ready to index files here.
                        ReindexFiles(folderSecond);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The process failed: {0}", e.ToString());
            }
        }
    }
}   