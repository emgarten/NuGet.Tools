using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace DGtoSolution
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine(".exe <path to dg> <output folder>");
            }

            var dgPath = Path.GetFullPath(args[0]);
            var outputFolder = Path.GetFullPath(args[1]);
            var pathContext = NuGetPathContext.Create(outputFolder);

            Directory.Delete(outputFolder, true);

            Directory.CreateDirectory(outputFolder);

            var onNuGet = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var repo = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            var findPackageById = repo.GetResource<FindPackageByIdResource>();
            var cacheContext = new SourceCacheContext();

            var inputDg = new DependencyGraphSpec(JObject.Parse(File.ReadAllText(dgPath)));
            var outputDg = new DependencyGraphSpec();
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in inputDg.Projects)
            {
                var name = project.Name.ToLowerInvariant() + "-" + Guid.NewGuid().ToString().Split("-")[0].ToLowerInvariant();
                var newPath = Path.Combine(outputFolder, name, name + ".csproj");
                mappings.Add(project.RestoreMetadata.ProjectUniqueName, newPath);
            }

            foreach (var item in inputDg.Restore)
            {
                // Copy new restore entry points
                outputDg.AddRestore(mappings[item]);
            }

            // Update file paths
            foreach (var oldProject in inputDg.Projects)
            {
                var project = oldProject.Clone();
                outputDg.AddProject(project);

                project.FilePath = mappings[project.FilePath];
                project.RestoreMetadata.ProjectUniqueName = mappings[project.RestoreMetadata.ProjectUniqueName];
                project.RestoreMetadata.ProjectPath = mappings[project.RestoreMetadata.ProjectPath];
                project.RestoreMetadata.OutputPath = Path.Combine(Path.GetDirectoryName(project.RestoreMetadata.ProjectUniqueName), "obj");

                // Clear user configs
                project.RestoreMetadata.ConfigFilePaths.Clear();
                project.RestoreMetadata.Sources.Clear();
                project.RestoreMetadata.Sources.Add(repo.PackageSource);

                project.RestoreMetadata.FallbackFolders.Clear();
                project.RestoreMetadata.PackagesPath = pathContext.UserPackageFolder;

                foreach (var tfm in project.RestoreMetadata.TargetFrameworks)
                {
                    foreach (var reference in tfm.ProjectReferences)
                    {
                        reference.ProjectPath = mappings[reference.ProjectPath];
                        reference.ProjectUniqueName = mappings[reference.ProjectUniqueName];
                    }
                }

                // Create project files
                var path = project.RestoreMetadata.ProjectUniqueName;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var doc = new XDocument();
                doc.Add(new XElement(XName.Get("Project"), new XAttribute(XName.Get("Sdk"), "Microsoft.NET.Sdk"), 
                    new XElement(XName.Get("PropertyGroup"),
                        new XElement(XName.Get("TargetFrameworks"), string.Join(";", project.RestoreMetadata.TargetFrameworks.Select(e => e.FrameworkName.GetShortFolderName()))),
                        new XElement(XName.Get("RuntimeIdentifiers"), string.Join(";", project.RuntimeGraph.Runtimes.Keys)))));

                foreach (var tfm in project.RestoreMetadata.TargetFrameworks)
                {
                    var condition = $" '$(TargetFramework)' == '{tfm.FrameworkName.GetShortFolderName()}' ";

                    var group = new XElement(XName.Get("ItemGroup"), new XAttribute(XName.Get("Condition"), condition));
                    doc.Root.Add(group);

                    foreach (var projPath in tfm.ProjectReferences)
                    {
                        var relativePath = PathUtility.GetRelativePath(path, projPath.ProjectUniqueName);

                        group.Add(new XElement(XName.Get("ProjectReference"), new XAttribute(XName.Get("Include"), relativePath)));
                    }

                    foreach (var pkg in project.GetTargetFramework(tfm.FrameworkName).Dependencies.ToArray())
                    {
                        // Check if this package is on nuget.org
                        if (onNuGet.GetOrAdd(pkg.Name, (id) =>
                         {
                             if (LocalFolderUtility.GetPackagesV3(pathContext.UserPackageFolder, pkg.Name, NullLogger.Instance).Any())
                             {
                                 Console.WriteLine($"Searching locally for {id}");
                                 return true;
                             }
                             else
                             {
                                 Console.WriteLine($"Searching nuget.org for {id}");
                                 return findPackageById.GetAllVersionsAsync(id, cacheContext, NullLogger.Instance, CancellationToken.None).Result.Any();
                             }
                         }))
                        {
                            group.Add(new XElement(XName.Get("PackageReference"),
                                new XAttribute(XName.Get("Include"), pkg.Name),
                                new XAttribute(XName.Get("Version"), pkg.LibraryRange.VersionRange.ToShortString())));
                        }
                        else
                        {
                            Console.WriteLine($"Removing package {pkg.Name} from {path}");
                            project.GetTargetFramework(tfm.FrameworkName).Dependencies.Remove(pkg);
                        }
                    }
                }

                doc.Save(path);
            }

            var dgOutPath = Path.Combine(outputFolder, "restore.dg");

            outputDg.Save(dgOutPath);

            // Write out an msbuild proj with everything
            var allTfms = inputDg.Projects.SelectMany(e => e.RestoreMetadata.TargetFrameworks.Select(f => f.FrameworkName)).Distinct().Select(e => e.GetShortFolderName());

            var rootDocPath = Path.Combine(outputFolder, "all.csproj");
            var rootDoc = new XDocument();
            rootDoc.Add(new XElement(XName.Get("Project"), new XAttribute(XName.Get("Sdk"), "Microsoft.NET.Sdk"),
                new XElement(XName.Get("PropertyGroup"),
                    new XElement(XName.Get("TargetFramework"), allTfms.First()),
                    new XElement(XName.Get("AssetTargetFallback"), string.Join(";", allTfms.Skip(1))))));

            var rootGroup = new XElement(XName.Get("ItemGroup"));
            rootDoc.Root.Add(rootGroup);

            foreach (var projectItem in outputDg.Restore)
            {
                var relativePath = PathUtility.GetRelativePath(rootDocPath, projectItem);
                rootGroup.Add(new XElement(XName.Get("ProjectReference"), new XAttribute(XName.Get("Include"), relativePath)));
            }

            rootDoc.Save(rootDocPath);
        }
    }
}
