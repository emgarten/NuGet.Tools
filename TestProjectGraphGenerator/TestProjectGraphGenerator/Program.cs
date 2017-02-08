using NuGet.Packaging;
using NuGet.Test.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestProjectGraphGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            GenerateInterlinkedPyramid(5, 5, 0).Wait();
        }

        /// <summary>
        /// Create a project graph with n levels. Each level references every project in ALL lower levels.
        /// This creates a large amount of overlap in the p2p references.
        ///                A
        ///              B   C
        ///            D  E F  G
        /// </summary>
        static async Task GenerateInterlinkedPyramid(int levels, int targetFrameworksPerProject, int packagesPerProject)
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                pathContext.CleanUp = false;
                Console.WriteLine($"generating: {pathContext.WorkingDirectory}");

                var rows = new Stack<List<ProjectNode>>();

                var frameworks = CreateFrameworks(targetFrameworksPerProject);
                var packages = await CreatePackages(packagesPerProject, pathContext);

                for (int depth = 0; depth < levels; depth++)
                {
                    var nodesInRow = Math.Pow(2, depth);

                    if (depth == 0)
                    {
                        nodesInRow = 1;
                    }

                    var row = new List<ProjectNode>();
                    rows.Push(row);

                    for (int column = 0; column < nodesInRow; column++)
                    {
                        var node = new ProjectNode()
                        {
                            Depth = depth,
                            Column = column,
                            Project = SimpleTestProjectContext.CreateNETCoreWithSDK($"d{depth}c{column}", pathContext.SolutionRoot, isToolingVersion15: true, frameworks: frameworks.ToArray())
                        };

                        node.Project.AddPackageToAllFrameworks(packages.ToArray());

                        row.Add(node);
                    }
                }

                var allProjects = rows.SelectMany(e => e).Select(e => e.Project).ToArray();

                var allChildrenSoFar = new HashSet<SimpleTestProjectContext>();

                // Link everything
                while (rows.Count > 1)
                {
                    var row = rows.Pop();
                    var parentRow = rows.Peek();

                    allChildrenSoFar.UnionWith(row.Select(e => e.Project));
                    var children = allChildrenSoFar.ToArray();

                    // Link every parent to all children in rows below
                    foreach (var parent in parentRow)
                    {
                        parent.Project.AddProjectToAllFrameworks(children);
                    }
                }

                // Add all projects to the solution
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot, allProjects.ToArray());
                solution.Create(pathContext.SolutionRoot);
            }
        }

        private static List<string> CreateFrameworks(int targetFrameworksPerProject)
        {
            var frameworks = new List<string>();

            for (int i = 0; i < targetFrameworksPerProject; i++)
            {
                frameworks.Add($"netstandard1.{i}");
            }

            return frameworks;
        }

        private static async Task<List<SimpleTestPackageContext>> CreatePackages(int packagesPerProject, SimpleTestPathContext pathContext)
        {
            var packages = new List<SimpleTestPackageContext>();

            for (int i = 0; i < packagesPerProject; i++)
            {
                packages.Add(new SimpleTestPackageContext($"package{i}", "1.0.0"));
            }

            // User global folder directly
            await SimpleTestPackageUtility.CreateFolderFeedV3(pathContext.UserPackagesFolder, PackageSaveMode.Defaultv3 | PackageSaveMode.Files, packages.ToArray());

            // Feed copy
            await SimpleTestPackageUtility.CreateFolderFeedV3(pathContext.PackageSource, PackageSaveMode.Defaultv3, packages.ToArray());
            return packages;
        }
    }
}