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

namespace NupkgParityLogger
{
    public class Program
    {
        private static FrameworkReducer reducer = new FrameworkReducer();
        private static NuGetFrameworkFullComparer comparer = new NuGetFrameworkFullComparer();

        private object _writerLockObj = new object();
        private object _purgeLockObj = new object();

        public void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine(".exe <nupkg folder path> <take>");
                Environment.Exit(10);
            }

            bool run = true;

            Console.CancelKeyPress += delegate {
                Console.WriteLine("shutting down");
                run = false;
            };

            DirectoryInfo nupkgDir = new DirectoryInfo(args[0]);
            int take = Int32.Parse(args[1]);

            if (take < 1)
            {
                take = Int32.MaxValue;
            }

            var files = new Stack<FileInfo>(nupkgDir.GetFiles("*.nupkg", SearchOption.AllDirectories).OrderBy(e => Guid.NewGuid()).Take(take));

            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 8;

            int count = 0;

            try
            {
                while (files.Count > 0)
                {
                    OptimizedZipPackage.PurgeCache();

                    var batch = new Stack<FileInfo>();

                    while (batch.Count < 100 && files.Count > 0)
                    {
                        batch.Push(files.Pop());
                    }

                    Parallel.ForEach(batch, options, file =>
                    {
                        if (run)
                        {
                            try
                            {
                                Console.WriteLine(file.Name);

                                PackageReader v3Reader = new PackageReader(file.OpenRead());
                                IPackage v2Reader = new OptimizedZipPackage(file.FullName);

                                TestDeps(v3Reader, v2Reader, file);

                                TestReferences(v3Reader, v2Reader, file);
                            }
                            catch (Exception ex)
                            {
                                lock (_writerLockObj)
                                {
                                    WriteToFile("exceptions", file.FullName + ".txt", ex.ToString());
                                }
                            }
                        }
                    });
                }
            }
            finally
            {
                OptimizedZipPackage.PurgeCache();
            }
        }

        //private static void TestFrameworkAssembly(PackageReader v3Package, IPackage v2Package, FileInfo file)
        //{
        //    StringBuilder data = new StringBuilder();

        //    var v3Groups = v3Package.GetFrameworkItems().ToArray();
        //    var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

        //    IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
        //    IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

        //    foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
        //    {
        //        var nearestV3 = reducer.GetNearest(project, v3Frameworks);
        //        NuGetFramework nearestV2 = null;

        //        var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3Groups, project, (e) => e.TargetFramework);
        //        if (v3Group != null)
        //        {
        //            v3Assemblies = v3Group.Items.OrderBy(e => e);
        //        }

        //        IEnumerable<FrameworkAssemblyReference> v2Group = null;
        //        if (VersionUtility.TryGetCompatibleItems<FrameworkAssemblyReference>(new FrameworkName(project.DotNetFrameworkName), v2Package.FrameworkAssemblies, out v2Group) && v2Group.Any())
        //        {
        //            v2Assemblies = v2Group.Select(e => e.AssemblyName).OrderBy(e => e);

        //            FrameworkName fwName = v2Group.Select(e => e.SupportedFrameworks.FirstOrDefault()).FirstOrDefault();

        //            if (fwName == null)
        //            {
        //                nearestV2 = NuGetFramework.AnyFramework;
        //            }
        //            else
        //            {
        //                nearestV2 = NuGetFramework.Parse(VersionUtility.GetShortFrameworkName(fwName));
        //            }
        //        }

        //        if (!v2Assemblies.SequenceEqual(v3Assemblies))
        //        {
        //            data.AppendLine("--------------------");
        //            data.AppendFormat("Project: {0}\r\n", FormatFramework(project));
        //            data.AppendFormat("V2:\r\n");

        //            foreach (string item in v2Assemblies)
        //            {
        //                data.AppendLine(item);
        //            }

        //            data.AppendFormat("V3:\r\n");

        //            foreach (string item in v3Assemblies)
        //            {
        //                data.AppendLine(item);
        //            }

        //            data.AppendLine("==================");
        //        }
        //    }

        //    WriteToFile("references", file.Name + ".txt", data.ToString());



        //    StringBuilder data = new StringBuilder();

        //    var v3RefGroups = v3Package.GetReferenceItems().ToArray();
        //    var v3Frameworks = v3RefGroups.Select(e => e.TargetFramework).ToArray();

        //    var v2Frameworks = v2Package.PackageAssemblyReferences.Select(e => e.TargetFramework).ToArray();

        //    foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
        //    {
        //        IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
        //        IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

        //        var nearestV3 = reducer.GetNearest(project, v3Frameworks);

        //        if (nearestV3 != null)
        //        {
        //            var v3Group = v3RefGroups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

        //            //var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3RefGroups, project, (e) => e.TargetFramework);
        //            if (v3Group != null)
        //            {
        //                v3Assemblies = v3Group.Items.OrderBy(e => e);
        //            }
        //        }

        //        var projectFwName = new FrameworkName(project.DotNetFrameworkName);

        //        IEnumerable<IPackageAssemblyReference> refSet = GetCompatibleItemsCore(projectFwName, v2Package.AssemblyReferences).ToList();

        //        if (refSet.Any())
        //        {
        //            v2Assemblies = refSet.Select(e => e.Path.Replace('\\', '/')).OrderBy(e => e);
        //        }

        //        if (!v2Assemblies.SequenceEqual(v3Assemblies))
        //        {
        //            data.AppendLine("--------------------");
        //            data.AppendFormat("Project: {0}\r\n", FormatFramework(project));
        //            data.AppendFormat("V2:\r\n");

        //            foreach (string item in v2Assemblies)
        //            {
        //                data.AppendLine(item);
        //            }

        //            data.AppendFormat("V3:\r\n");

        //            foreach (string item in v3Assemblies)
        //            {
        //                data.AppendLine(item);
        //            }

        //            data.AppendLine("==================");
        //        }
        //    }

        //    WriteToFile("framework", file.Name + ".txt", data.ToString());
        //}

        private static void TestReferences(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v3RefGroups = v3Package.GetReferenceItems().ToArray();
            var v3Frameworks = v3RefGroups.Select(e => e.TargetFramework).ToArray();

            var v2Frameworks = v2Package.PackageAssemblyReferences.Select(e => e.TargetFramework).ToArray();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
                IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

                var nearestV3 = reducer.GetNearest(project, v3Frameworks);

                if (nearestV3 != null)
                {
                    var v3Group = v3RefGroups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

                    //var v3Group = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(v3RefGroups, project, (e) => e.TargetFramework);
                    if (v3Group != null)
                    {
                        v3Assemblies = v3Group.Items.OrderBy(e => e);
                    }
                }

                var projectFwName = new FrameworkName(project.DotNetFrameworkName);

                IEnumerable<IPackageAssemblyReference> refSet = GetCompatibleItemsCore(projectFwName, v2Package.AssemblyReferences).ToList();

                if (refSet.Any())
                {
                    v2Assemblies = refSet.Select(e => e.Path.Replace('\\', '/')).OrderBy(e => e);
                }

                if (!v2Assemblies.SequenceEqual(v3Assemblies))
                {
                    data.AppendLine("--------------------");
                    data.AppendFormat("Project: {0}\r\n", FormatFramework(project));
                    data.AppendFormat("V2:\r\n");

                    foreach (string item in v2Assemblies)
                    {
                        data.AppendLine(item);
                    }

                    data.AppendFormat("V3:\r\n");

                    foreach (string item in v3Assemblies)
                    {
                        data.AppendLine(item);
                    }

                    data.AppendLine("==================");
                }
            }

            WriteToFile("references", file.Name + ".txt", data.ToString());
        }

        private static void TestDeps(PackageReader v3Pkg, IPackage v2Pkg, FileInfo file)
        {
            var v3Deps = v3Pkg.GetPackageDependencies();

            var v2Deps = v2Pkg.DependencySets;

            StringBuilder data = new StringBuilder();

            if (v3Deps.Count() != v2Deps.Count())
            {
                data.AppendFormat("Dependency group counts  v3: {0} v2: {1}\n", v3Deps.Count(), v2Deps.Count());
            }

            var v3Groups = v3Pkg.GetPackageDependencies().ToArray();
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

                IEnumerable<PackageDependency> v2Group = v2Pkg.GetCompatiblePackageDependencies(new FrameworkName(project.DotNetFrameworkName));

                if (v2Group != null && v2Group.Any())
                {
                    v2Items = v2Group.Select(e => e.Id).OrderBy(e => e);
                }

                if (!v2Items.SequenceEqual(v3Items))
                {
                    data.AppendLine("--------------------");
                    data.AppendFormat("Project: {0}\r\n", FormatFramework(project));
                    data.AppendFormat("V2:\r\n");

                    foreach (string item in v2Items)
                    {
                        data.AppendLine(item);
                    }

                    data.AppendFormat("V3:\r\n");

                    foreach (string item in v3Items)
                    {
                        data.AppendLine(item);
                    }

                    data.AppendLine("==================");
                }
            }

            WriteToFile("deps", file.Name + ".txt", data.ToString());
        }

        private static IEnumerable<T> GetCompatibleItemsCore<T>(FrameworkName projectFramework, IEnumerable<T> items) where T : NuGet.IFrameworkTargetable
        {
            IEnumerable<T> compatibleItems;
            if (VersionUtility.TryGetCompatibleItems(projectFramework, items, out compatibleItems))
            {
                return compatibleItems;
            }
            return Enumerable.Empty<T>();
        }

        private static void WriteToFile(string folder, string file, string data)
        {
            if (!String.IsNullOrEmpty(data))
            {
                string root = @"C:\output";

                string path = Path.Combine(root, folder, file);

                Console.WriteLine(path);

                FileInfo outputFile = new FileInfo(path);

                if (!outputFile.Directory.Exists)
                {
                    outputFile.Directory.Create();
                }

                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.Write(data);
                }
            }
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
            //"portable-net45+win8",
            //"portable-net4+sl4+wp71+win8",
            //"portable-net4+sl4+wp71+win8+monoandroid+monotouch",
            //"",
        };
    }
}
