using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.CatalogReader
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var reader = new CatalogReader(new Uri("https://api.nuget.org/v3/catalog0/index.json"));

            var entries = reader.GetRolledUpEntries(CancellationToken.None).Result;

            var regex = new Regex(@"^(?<Version>\d+(\s*\.\s*\d+){0,3})(?<Release>-[a-z][0-9a-z-]*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

            foreach (var entry in entries)
            {
                var s = entry.Version.ToFullString();

                var match = regex.Match(s);

                if (!match.Success)
                {
                    Console.WriteLine($"{entry.Id} {s} {entry.CommitTimeStamp}");
                }
            }
        }
    }
}
