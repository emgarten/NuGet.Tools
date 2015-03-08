extern alias Legacy;
using NuGetV2 = Legacy.NuGet;
using NuGet.Frameworks;
using NuGet.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Versioning;

namespace NupkgParity
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine(".exe <nupkg folder path>");
                Environment.Exit(10);
            }

            DirectoryInfo nupkgDir = new DirectoryInfo(args[0]);

            var reducer = new FrameworkReducer();
            var comparer = new NuGetFrameworkFullComparer();

            foreach (FileInfo file in nupkgDir.GetFiles("*.nupkg", SearchOption.AllDirectories))
            {
                PackageReader v3Reader = new PackageReader(file.OpenRead());

                var v3Frameworks = v3Reader.GetReferenceItems().Select(e => e.TargetFramework).ToArray();

                NuGetV2.IPackage v2Reader = new NuGetV2.ZipPackage(file.FullName);

                var v2Frameworks = v2Reader.PackageAssemblyReferences.Select(e => e.TargetFramework).ToArray();

                foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
                {
                    var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                    NuGetFramework nearestV2 = null;

                    IEnumerable<NuGetV2.IPackageAssemblyReference> refSet = null;
                    if (NuGetV2.VersionUtility.TryGetCompatibleItems<NuGetV2.IPackageAssemblyReference>(new FrameworkName(project.DotNetFrameworkName), v2Reader.AssemblyReferences, out refSet) && refSet.Any())
                    {
                        FrameworkName fwName = refSet.Select(e => e.TargetFramework).Distinct().Single();

                        if (fwName == null)
                        {
                            nearestV2 = NuGetFramework.AnyFramework;
                        }
                        else
                        {
                            nearestV2 = NuGetFramework.Parse(fwName.ToString());
                        }
                    }

                    if (comparer.Equals(nearestV2, nearestV3))
                    {

                    }
                    else
                    {
                        Console.WriteLine("Package: {0} Project: {1} V2: {2} V3: {3}", 
                            file.Name, project.GetShortFolderName(), nearestV2 == null ? "null" : nearestV2.GetShortFolderName(), nearestV3 == null ? "null" : nearestV3.GetShortFolderName());
                    }
                }
            }
        }

        static List<string> ProjectFrameworks = new List<string>()
        {
            "net20",
            "net35",
            "net40",
            "net45",
            "net452",
            "net451",
            "net35-client",
            "net40-client",
            "net45-client",
            "net45-full",
            "net40-full",
            "win8",
            "win81",
            "win",
            "sl4",
            "sl5",
            "native",
            "wpa81",
            //"monoandroid",
            //"monotouch",
            "portable-net4+sl4+wp71+win8",
            "portable-net4+sl4+wp71+win8+monoandroid+monotouch",
            //"",
        };
    }
}
