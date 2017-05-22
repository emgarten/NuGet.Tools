using NuGet.Client;
using NuGet.ContentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NuGetAssetsReader
{
    internal static class Extensions
    {
        internal static IEnumerable<PatternSet> GetAllPatterns(this ManagedCodeConventions conventions)
        {
            yield return conventions.Patterns.RuntimeAssemblies;
            yield return conventions.Patterns.CompileRefAssemblies;
            yield return conventions.Patterns.CompileLibAssemblies;
            yield return conventions.Patterns.NativeLibraries;
            yield return conventions.Patterns.ResourceAssemblies;
            yield return conventions.Patterns.MSBuildFiles;
            yield return conventions.Patterns.MSBuildMultiTargetingFiles;
            //yield return conventions.Patterns.ContentFiles;
        }

        internal static IEnumerable<PatternSet> GetRIDPatterns(this ManagedCodeConventions conventions)
        {
            yield return conventions.Patterns.RuntimeAssemblies;
            yield return conventions.Patterns.NativeLibraries;
            yield return conventions.Patterns.ResourceAssemblies;
        }

        internal static IEnumerable<string> GetRID(this ContentItemGroup group)
        {
            if (group.Properties.TryGetValue(ManagedCodeConventions.PropertyNames.RuntimeIdentifier, out var obj))
            {
                return new string[] { (string)obj };
            }

            return Enumerable.Empty<string>();
        }
    }
}
