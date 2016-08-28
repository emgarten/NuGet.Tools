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

            var entries = reader.GetEntries(DateTimeOffset.Parse("2016-07-30T00:05:22.6952071Z"), DateTimeOffset.Parse("2016-07-30T00:06:21.8534934Z"), CancellationToken.None).Result;

            foreach (var entry in entries)
            {
                Console.WriteLine(entry);
            }
        }
    }
}
