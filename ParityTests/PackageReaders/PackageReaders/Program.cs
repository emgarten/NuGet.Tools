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
using System.Diagnostics;

namespace PackageReaders
{
    public class Program
    {
        private static FrameworkReducer reducer = new FrameworkReducer();
        private static NuGetFrameworkFullComparer comparer = new NuGetFrameworkFullComparer();

        private static DirectoryInfo _output = new DirectoryInfo(@"C:\output");

        private static object _writerLockObj = new object();
        private static object _purgeLockObj = new object();

        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine(".exe <nupkg folder path> <take>");
                Environment.Exit(10);
            }

            if (_output.Exists)
            {
                _output.Delete(true);
            }

            _output.Create();

            bool run = true;

            Console.CancelKeyPress += delegate
            {
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
            object lockObj = new object();
            Stopwatch timer = new Stopwatch();
            timer.Start();

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
                            lock (lockObj)
                            {
                                count++;

                                if (count % 10000 == 0)
                                {
                                    Console.WriteLine("Done: {0} Remaining: {1} Time: {2}", count, files.Count, timer.Elapsed);
                                }
                            }

                            try
                            {
                                // Console.WriteLine(file.Name);

                                IPackage v2Reader = null;

                                try
                                {
                                    v2Reader = new OptimizedZipPackage(file.FullName);
                                }
                                catch
                                {
                                    // ignore previously bad packages
                                }

                                if (v2Reader != null)
                                {
                                    PackageReader v3Reader = new PackageReader(file.OpenRead());

                                    TestDeps(v3Reader, v2Reader, file);

                                    TestReferences(v3Reader, v2Reader, file);

                                    TestFrameworkAssembly(v3Reader, v2Reader, file);

                                    TestBuildFiles(v3Reader, v2Reader, file);

                                    TestContentFiles(v3Reader, v2Reader, file);

                                    TestToolFiles(v3Reader, v2Reader, file);
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (_writerLockObj)
                                {
                                    WriteToFile("exceptions", file.Name + ".txt", ex.ToString());
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

            Console.WriteLine(timer.Elapsed);

            Console.WriteLine("done");
            Console.ReadKey();
        }

        private static void TestToolFiles(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v3Groups = v3Package.GetToolItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
                IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

                var nearestV3 = reducer.GetNearest(project, v3Frameworks);

                if (nearestV3 != null)
                {
                    var v3Group = v3Groups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

                    if (v3Group != null)
                    {
                        v3Assemblies = v3Group.Items.OrderBy(e => e);
                    }
                }

                var projectFwName = new FrameworkName(project.DotNetFrameworkName);

                IEnumerable<IPackageFile> v2Group = GetCompatibleItemsCore(projectFwName, v2Package.GetToolFiles()).ToList();

                if (v2Group.Any())
                {
                    v2Assemblies = v2Group.Select(e => e.Path.Replace('\\', '/')).Where(e => !e.EndsWith("/_._")).OrderBy(e => e);
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

            WriteToFile("tools", file.Name + ".txt", data.ToString());
        }

        private static void TestContentFiles(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v3Groups = v3Package.GetContentItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            // var v2Frameworks = v2Package.GetBuildFiles().SelectMany(e => e.SupportedFrameworks).ToArray();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
                IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

                var nearestV3 = reducer.GetNearest(project, v3Frameworks);

                if (nearestV3 != null)
                {
                    var v3Group = v3Groups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

                    if (v3Group != null)
                    {
                        v3Assemblies = v3Group.Items.OrderBy(e => e);
                    }
                }

                var projectFwName = new FrameworkName(project.DotNetFrameworkName);

                IEnumerable<IPackageFile> v2Group = GetCompatibleItemsCore(projectFwName, v2Package.GetContentFiles()).ToList();

                if (v2Group.Any())
                {
                    v2Assemblies = v2Group.Select(e => e.Path.Replace('\\', '/')).Where(e => !e.EndsWith("/_._")).OrderBy(e => e);
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

            WriteToFile("content", file.Name + ".txt", data.ToString());
        }

        private static void TestBuildFiles(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v3Groups = v3Package.GetBuildItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();

            // var v2Frameworks = v2Package.GetBuildFiles().SelectMany(e => e.SupportedFrameworks).ToArray();

            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
                IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

                var nearestV3 = reducer.GetNearest(project, v3Frameworks);

                if (nearestV3 != null)
                {
                    var v3Group = v3Groups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

                    if (v3Group != null)
                    {
                        v3Assemblies = v3Group.Items.OrderBy(e => e);
                    }
                }

                var projectFwName = new FrameworkName(project.DotNetFrameworkName);

                IEnumerable<IPackageFile> v2Group = GetCompatibleItemsCore(projectFwName, v2Package.GetBuildFiles()).ToList();

                if (v2Group.Any())
                {
                    v2Assemblies = v2Group.Select(e => e.Path.Replace('\\', '/')).Where(e => !e.EndsWith("/_._")).OrderBy(e => e);
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

            WriteToFile("build", file.Name + ".txt", data.ToString());
        }

        private static void TestFrameworkAssembly(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v2Frameworks = v2Package.FrameworkAssemblies.SelectMany(e => e.SupportedFrameworks).ToArray();

            var v3Groups = v3Package.GetFrameworkItems().ToArray();
            var v3Frameworks = v3Groups.Select(e => e.TargetFramework).ToArray();


            foreach (var project in ProjectFrameworks.Select(s => NuGetFramework.Parse(s)))
            {
                IEnumerable<string> v2Assemblies = Enumerable.Empty<string>();
                IEnumerable<string> v3Assemblies = Enumerable.Empty<string>();

                var nearestV3 = reducer.GetNearest(project, v3Frameworks);

                if (nearestV3 != null)
                {
                    var v3Group = v3Groups.Where(e => comparer.Equals(e.TargetFramework, nearestV3)).FirstOrDefault();

                    if (v3Group != null)
                    {
                        v3Assemblies = v3Group.Items.OrderBy(e => e);
                    }
                }

                var projectFwName = new FrameworkName(project.DotNetFrameworkName);

                IEnumerable<FrameworkAssemblyReference> v2Group = GetCompatibleItemsCore(projectFwName, v2Package.FrameworkAssemblies).ToList();

                if (v2Group.Any())
                {
                    v2Assemblies = v2Group.Select(e => e.AssemblyName).OrderBy(e => e);
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

            WriteToFile("framework", file.Name + ".txt", data.ToString());
        }

        private static void TestReferences(PackageReader v3Package, IPackage v2Package, FileInfo file)
        {
            StringBuilder data = new StringBuilder();

            var v2Frameworks = v2Package.PackageAssemblyReferences.Select(e => e.TargetFramework).ToArray();

            var v3RefGroups = v3Package.GetReferenceItems().ToArray();
            var v3Frameworks = v3RefGroups.Select(e => e.TargetFramework).ToArray();


            //if (v3RefGroups.Count() != v2Package.PackageAssemblyReferences.Count())
            //{
            //    data.AppendFormat("Reference group counts  v3: {0} v2: {1}\n", v3RefGroups.Count(), v2Package.PackageAssemblyReferences.Count());
            //}

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

                var assemblyReferences = GetCompatibleItemsCore(projectFwName, v2Package.AssemblyReferences);
                var packageReferences = GetCompatibleItemsCore(projectFwName, v2Package.PackageAssemblyReferences).FirstOrDefault();

                if (packageReferences != null)
                {
                    assemblyReferences = assemblyReferences.Where(assembly => packageReferences.References.Contains(assembly.Name, StringComparer.OrdinalIgnoreCase));
                }

                IEnumerable<IPackageAssemblyReference> refSet = assemblyReferences.ToList();

                if (refSet.Any())
                {
                    v2Assemblies = refSet.Select(e => e.Path.Replace('\\', '/')).Where(e => !e.EndsWith("/_._")).OrderBy(e => e);
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
            var v2Deps = v2Pkg.DependencySets;

            var v3Deps = v3Pkg.GetPackageDependencies();


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
                string path = Path.Combine(_output.FullName, folder, file);

                FileInfo fileInfo = new FileInfo(path);

                if (!fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                Console.WriteLine(path);

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
            "net451",
            "net452",
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
            "aspnetcore50",
            "aspnet50",
            //"portable-net45+win8",
            //"portable-net4+sl4+wp71+win8",
            //"portable-net4+sl4+wp71+win8+monoandroid+monotouch",
            //"",
        };
    }
}
