using Dasync.Collections;
using ShellProgressBar;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Channel_Grabber
{
    public static class StringExtensions
    {
        public static bool ContainsIgnoreCase(this string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
    class Program
    {
        static bool ContainsStream(string input, string network, string region)
        {
            if (region != "*")
            {
                RegionInfo regionInfo = new RegionInfo(region);
                return (input.Contains(regionInfo.TwoLetterISORegionName) || input.Contains(regionInfo.ThreeLetterISORegionName)) && input.ContainsIgnoreCase(network);
            }
            else
            {
                return input.ContainsIgnoreCase(network);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool ReadConsoleW(IntPtr hConsoleInput, [Out] byte[]
           lpBuffer, uint nNumberOfCharsToRead, out uint lpNumberOfCharsRead,
           IntPtr lpReserved);

        static string ReadLine()
        {
            var buf = new byte[1024];
            ReadConsoleW(GetStdHandle(-10), buf, 1024, out uint read, (IntPtr)0);
            return Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, buf.Take(((int)read - 2) * 2).ToArray()));
        }

        static async Task<string> IteratePlaylists(List<string> playlists, string network, string region)
        {
            string hits = "";
            using (ShellProgressBar.ProgressBar progressBar = new ShellProgressBar.ProgressBar(playlists.Count, "", ConsoleColor.Green))
            {
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                await playlists.ParallelForEachAsync(async playlist =>
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    string[] lines = File.ReadAllLines(playlist);
                    using (ChildProgressBar child = progressBar.Spawn(lines.Length, "processing " + Path.GetFileName(playlist), new ProgressBarOptions { ProgressCharacter = '-', CollapseWhenFinished = true }))
                    {
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (ContainsStream(lines[i], network, region)) // check if line contains a stream (country code and network)
                            {
                                try
                                {
                                    if (!String.IsNullOrEmpty(await client.GetStringAsync(lines[i+1])))
                                        hits += lines[i] + "\n" + lines[i+1] + "\n";
                                } // take the link that should be after the line, test if working and not blank response
                                catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException) { } // some links are dumb, this fixes program from breaking from dumb links
                            }
                            child.Tick();
                        }
                    }
                    progressBar.Tick();
                }, maxDegreeOfParallelism: Environment.ProcessorCount * 5);
            }
            return hits;
        }

        [STAThread]
        static void Main()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.InputEncoding = Encoding.UTF8; // UTF8 encoding, ReadLine method, etc. is used to support channel names like "Çufo"
            if (!Directory.Exists("result"))
                Directory.CreateDirectory("result");
            Console.WriteLine("Type the network you want to dump..");
            string network = ReadLine();
            Console.WriteLine("Type the country code you want to dump (* for all countries)..");
            string region = Console.ReadLine();
            if (region != "*") // if not * wildcard for region, verifies if region exists
            {
                try
                {
                    RegionInfo regionInfo = new RegionInfo(region);
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Invalid region! Exiting...");
                    Thread.Sleep(3000);
                    Environment.Exit(0);
                }
            }
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.ShowNewFolderButton = true;
                folderDialog.Description = "Select a folder with IPTV playlists...";
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    List<string> playlists = Directory
                        .EnumerateFiles(folderDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => file.ToLower().EndsWith("m3u") || file.ToLower().EndsWith("m3u8"))
                        .ToList(); // enumerate through directory, m3u and m3u8 files only

                    string hits = IteratePlaylists(playlists, network, region).Result;
                    if(hits.Length != 0)
                        File.CreateText("result\\result.txt").WriteLine(hits);
                    else
                        Console.WriteLine("Nothing was found...");
                }
            }

            Console.WriteLine("Done! Press any key to exit.");
            Console.ReadKey();
        }
    }
}