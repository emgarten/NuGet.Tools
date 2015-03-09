using NuGet.Frameworks;
using NuGet.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Collections.Concurrent;
using NuGet;

namespace NupkgParity
{
    class Program
    {
        static FrameworkReducer reducer = new FrameworkReducer();
        static NuGetFrameworkFullComparer comparer = new NuGetFrameworkFullComparer();

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine(".exe <nupkg folder path>");
                Environment.Exit(10);
            }

            bool run = true;

            Console.CancelKeyPress += delegate {
                run = false;
            };


            DirectoryInfo nupkgDir = new DirectoryInfo(args[0]);

            ConcurrentBag<NupkgDifference> diffs = new ConcurrentBag<NupkgDifference>();
            ConcurrentBag<string> exceptions = new ConcurrentBag<string>();

            var files = nupkgDir.GetFiles("*.nupkg", SearchOption.AllDirectories).OrderBy(e => Guid.NewGuid()).ToArray();

            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 8;

            try
            {
                Parallel.ForEach(files, options, file =>
                {
                    if (run)
                    {
                        try
                        {
                            Console.WriteLine(file.Name);

                            PackageReader v3Reader = new PackageReader(file.OpenRead());
                            IPackage v2Reader = new ZipPackage(file.FullName);

                            var refTest = ReferenceAssemblyTest(v2Reader, v3Reader);
                            if (refTest != null)
                            {
                                refTest.Package = file.Name;
                                diffs.Add(refTest);
                            }

                            var gacTest = FrameworkAssemblyTest(v2Reader, v3Reader);
                            if (gacTest != null)
                            {
                                gacTest.Package = file.Name;
                                diffs.Add(gacTest);
                            }

                            var buildTest = BuildItemsTest(v2Reader, v3Reader);
                            if (buildTest != null)
                            {
                                buildTest.Package = file.Name;
                                diffs.Add(buildTest);
                            }

                            var contentTest = ContentItemsTest(v2Reader, v3Reader);
                            if (contentTest != null)
                            {
                                contentTest.Package = file.Name;
                                diffs.Add(contentTest);
                            }

                            var depTest = DepTest(v2Reader, v3Reader);
                            if (depTest != null)
                            {
                                depTest.Package = file.Name;
                                diffs.Add(depTest);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                            exceptions.Add(file.Name + "," + ex.ToString());
                        }
                    }
                });
            }
            finally
            {
                if (exceptions.Any())
                {
                    using (StreamWriter writer = new StreamWriter("nupkgparity-crashes.csv", false))
                    {
                        foreach (string s in exceptions)
                        {
                            writer.WriteLine(s);
                        }
                    }
                }

                foreach (var testGroup in diffs.GroupBy(e => e.Test))
                {
                    string report = "nupkgparity-" + testGroup.Key + ".csv";
                    Console.WriteLine("Writing: " + report);
                    using (StreamWriter writer = new StreamWriter(report, false))
                    {
                        writer.WriteLine("project,v2,v3,count,nupkgs");

                        foreach (var projectGroup in testGroup.GroupBy(e => e.Project + "|" + e.V2Framework + "|" + e.V3Framework).OrderBy(e => e.Key))
                        {
                            var first = projectGroup.First();

                            string s = String.Format("{0},{1},{2},{3},{4}", first.Project, first.V2Framework,
                                    first.V3Framework, projectGroup.Count(), String.Join(" ", projectGroup.Select(e => e.Package).OrderBy(e => e)));
                            writer.WriteLine(s);
                        }
                    }
                }
            }
        }

        private static NupkgDifference ContentItemsTest(IPackage v2Package, PackageReader v3Package)
        {
            NupkgDifference diff = null;

            var v3Groups = v3Package.GetContentItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            IEnumerable<string> v2Items = Enumerable.Empty<string>();
            IEnumerable<string> v3Items = Enumerable.Empty<string>();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                NuGetFramework nearestV2 = null;

                var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3Groups, project, (e) => e.TargetFramework);
                if (v3Group != null)
                {
                    v3Items = v3Group.Items.OrderBy(e => e);
                }

                IEnumerable<IPackageFile> v2Group = null;
                if (VersionUtility.TryGetCompatibleItems<IPackageFile>(new FrameworkName(project.DotNetFrameworkName), v2Package.GetContentFiles(), out v2Group) && v2Group.Any())
                {
                    v2Items = v2Group.Select(e => e.Path.Replace('\\', '/')).OrderBy(e => e);

                    FrameworkName fwName = v2Group.Select(e => e.SupportedFrameworks.FirstOrDefault()).FirstOrDefault();

                    if (fwName == null)
                    {
                        nearestV2 = NuGetFramework.AnyFramework;
                    }
                    else
                    {
                        nearestV2 = NuGetFramework.Parse(VersionUtility.GetShortFrameworkName(fwName));
                    }
                }

                if (!v2Items.SequenceEqual(v3Items))
                {
                    diff = new NupkgDifference()
                    {
                        Project = FormatFramework(project),
                        Test = "ContentFiles",
                        V2Framework = FormatFramework(nearestV2),
                        V3Framework = FormatFramework(nearestV3),
                    };
                }
            }

            return diff;
        }

        private static NupkgDifference BuildItemsTest(IPackage v2Package, PackageReader v3Package)
        {
            NupkgDifference diff = null;

            var v3Groups = v3Package.GetBuildItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            IEnumerable<string> v2Items = Enumerable.Empty<string>();
            IEnumerable<string> v3Items = Enumerable.Empty<string>();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                NuGetFramework nearestV2 = null;

                var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3Groups, project, (e) => e.TargetFramework);
                if (v3Group != null)
                {
                    v3Items = v3Group.Items.OrderBy(e => e);
                }

                IEnumerable<IPackageFile> v2Group = null;
                if (VersionUtility.TryGetCompatibleItems<IPackageFile>(new FrameworkName(project.DotNetFrameworkName), v2Package.GetBuildFiles(), out v2Group) && v2Group.Any())
                {
                    v2Items = v2Group.Select(e => e.Path.Replace('\\', '/')).OrderBy(e => e);

                    FrameworkName fwName = v2Group.Select(e => e.SupportedFrameworks.FirstOrDefault()).FirstOrDefault();

                    if (fwName == null)
                    {
                        nearestV2 = NuGetFramework.AnyFramework;
                    }
                    else
                    {
                        nearestV2 = NuGetFramework.Parse(VersionUtility.GetShortFrameworkName(fwName));
                    }
                }

                if (!v2Items.SequenceEqual(v3Items))
                {
                    diff = new NupkgDifference()
                    {
                        Project = FormatFramework(project),
                        Test = "BuildFiles",
                        V2Framework = FormatFramework(nearestV2),
                        V3Framework = FormatFramework(nearestV3),
                    };
                }
            }

            return diff;
        }

        private static NupkgDifference DepTest(IPackage v2Package, PackageReader v3Package)
        {
            NupkgDifference diff = null;

            var v3Groups = v3Package.GetPackageDependencies().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            IEnumerable<string> v2Items = Enumerable.Empty<string>();
            IEnumerable<string> v3Items = Enumerable.Empty<string>();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                NuGetFramework nearestV2 = null;

                var v3Group = NuGetFrameworkUtility.GetNearest<PackageDependencyGroup>(v3Groups, project, (e) => e.TargetFramework);
                if (v3Group != null)
                {
                    v3Items = v3Group.Packages.Select(e => e.Id).OrderBy(e => e);
                }

                IEnumerable<PackageDependency> v2Group = v2Package.GetCompatiblePackageDependencies(new FrameworkName(project.DotNetFrameworkName));

                if (v2Group != null && v2Group.Any())
                {
                    v2Items = v2Group.Select(e => e.Id).OrderBy(e => e);

                    nearestV2 = new NuGetFramework("TODO");
                }

                if (!v2Items.SequenceEqual(v3Items))
                {
                    diff = new NupkgDifference()
                    {
                        Project = FormatFramework(project),
                        Test = "PackageDependencies",
                        V2Framework = FormatFramework(nearestV2),
                        V3Framework = FormatFramework(nearestV3),
                    };
                }
            }

            return diff;
        }

        private static NupkgDifference FrameworkAssemblyTest(IPackage v2Package, PackageReader v3Package)
        {
            NupkgDifference diff = null;

            var v3Groups = v3Package.GetFrameworkItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
            IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                NuGetFramework nearestV2 = null;

                var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3Groups, project, (e) => e.TargetFramework);
                if (v3Group != null)
                {
                    v3Assemblies = v3Group.Items.OrderBy(e => e);
                }

                IEnumerable<FrameworkAssemblyReference> v2Group = null;
                if (VersionUtility.TryGetCompatibleItems<FrameworkAssemblyReference>(new FrameworkName(project.DotNetFrameworkName), v2Package.FrameworkAssemblies, out v2Group) && v2Group.Any())
                {
                    v2Assemblies = v2Group.Select(e => e.AssemblyName).OrderBy(e => e);

                    FrameworkName fwName = v2Group.Select(e => e.SupportedFrameworks.FirstOrDefault()).FirstOrDefault();

                    if (fwName == null)
                    {
                        nearestV2 = NuGetFramework.AnyFramework;
                    }
                    else
                    {
                        nearestV2 = NuGetFramework.Parse(VersionUtility.GetShortFrameworkName(fwName));
                    }
                }

                if (!v2Assemblies.SequenceEqual(v3Assemblies))
                {
                    diff = new NupkgDifference()
                    {
                        Project = FormatFramework(project),
                        Test = "FrameworkAssem",
                        V2Framework = FormatFramework(nearestV2),
                        V3Framework = FormatFramework(nearestV3),
                    };
                }
            }

            return diff;
        }

        private static NupkgDifference ReferenceAssemblyTest(IPackage v2Package, PackageReader v3Package)
        {
            NupkgDifference diff = null;

            var v3RefGroups = v3Package.GetReferenceItems().ToArray();
            var v3Frameworks = v3RefGroups.Select(e => e.TargetFramework).ToArray();

            var v2Frameworks = v2Package.PackageAssemblyReferences.Select(e => e.TargetFramework).ToArray();

            IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
            IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                var nearestV3 = reducer.GetNearest(project, v3Frameworks);
                NuGetFramework nearestV2 = null;

                var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3RefGroups, project, (e) => e.TargetFramework);
                if (v3Group != null)
                {
                    v3Assemblies = v3Group.Items.OrderBy(e => e);
                }

                IEnumerable<IPackageAssemblyReference> refSet = null;
                if (VersionUtility.TryGetCompatibleItems<IPackageAssemblyReference>(new FrameworkName(project.DotNetFrameworkName), v2Package.AssemblyReferences, out refSet) && refSet.Any())
                {
                    v2Assemblies = refSet.Select(e => e.Path.Replace('\\', '/')).OrderBy(e => e);

                    FrameworkName fwName = refSet.Select(e => e.TargetFramework).Distinct().Single();

                    if (fwName == null)
                    {
                        nearestV2 = NuGetFramework.AnyFramework;
                    }
                    else
                    {
                        nearestV2 = NuGetFramework.Parse(VersionUtility.GetShortFrameworkName(fwName));
                    }
                }

                if (!v2Assemblies.SequenceEqual(v3Assemblies))
                {
                    diff = new NupkgDifference()
                    {
                        Project = FormatFramework(project),
                        Test = "ReferenceAssem",
                        V2Framework = FormatFramework(nearestV2),
                        V3Framework = FormatFramework(nearestV3),
                    };
                }
            }

            return diff;
        }

        static string FormatFramework(NuGetFramework framework)
        {
            string s = "null";

            if (framework != null)
            {
                s = framework.GetShortFolderName();
            }

            return s;
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
            //"aspnetcore50",
            //"aspnet50",
            "portable-net45+win8",
            "portable-net4+sl4+wp71+win8",
            "portable-net4+sl4+wp71+win8+monoandroid+monotouch",
            "",
        };
    }
}
