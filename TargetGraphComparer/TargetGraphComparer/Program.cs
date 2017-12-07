using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.ProjectModel;

namespace TargetGraphComparer
{
    class Program
    {
        static void Main(string[] args)
        {
            var format = new LockFileFormat();
            var assetsFile = format.Read(args[0]);

            foreach (var group in assetsFile.Targets.GroupBy(e => e.TargetFramework).ToArray())
            {
                var ridless = group.Single(e => string.IsNullOrEmpty(e.RuntimeIdentifier));
                var librariesInRidless = new HashSet<string>(ridless.Libraries.Select(e => $"{e.Name}/{e.Version.ToNormalizedString()}".ToLowerInvariant()));

                var withRids = group.Where(e => !string.IsNullOrEmpty(e.RuntimeIdentifier)).ToArray();

                foreach (var graph in withRids)
                {
                    Console.WriteLine(graph.Name);
                    var librariesInRidGraph = new HashSet<string>(graph.Libraries.Select(e => $"{e.Name}/{e.Version.ToNormalizedString()}".ToLowerInvariant()));

                    var ridlessOnly = librariesInRidless.Except(librariesInRidGraph).ToList();
                    var ridOnly = librariesInRidGraph.Except(librariesInRidless).ToList();

                    foreach (var item in ridOnly)
                    {
                        Console.WriteLine("RID specific: " + item);
                    }

                    foreach (var item in ridlessOnly)
                    {
                        Console.WriteLine("RIDless only: " + item);
                    }
                }
            }
        }
    }
}
