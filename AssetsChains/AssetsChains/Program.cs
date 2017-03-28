using System;
using System.Collections.Generic;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using System.Linq;

namespace AssetsChains
{
    class Program
    {
        static void Main(string[] args)
        {

            LockFileFormat format = new LockFileFormat();

            var assetsFile = format.Read(@"bad.json");

            var target = assetsFile.GetTarget(NuGetFramework.Parse("uap10.0"), "win10-x86");

            var roots = target.Libraries.Where(e => GetParents(target, e).Count == 0).ToList();

            var chains = GetChains(target);

            foreach (var lib in target.Libraries.Where(e => e.Name.Equals("System.Net.Primitives", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var withId = GetChainsWithId(chains, lib.Name);

                var min = withId.Select(e => GetDepthInChain(e, lib.Name)).Min();

                Console.WriteLine($"{lib.Name} {lib.Version} [{min}]");

                foreach (var chain in withId)
                {
                    var depth = GetDepthInChain(chain, lib.Name);

                    if (depth < 3)
                    {
                        Console.WriteLine(string.Join(" -> ", chain.Reverse().Select(e => $"{e.Name} {e.Version}")));
                    }
                }
            }

        }

        static List<LockFileTargetLibrary> GetParents(LockFileTarget target, LockFileTargetLibrary child)
        {
            return target.Libraries.Where(e => e.Dependencies.Any(d => d.Id.Equals(child.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        static LockFileTargetLibrary GetId(LockFileTarget target, string id)
        {
            return target.Libraries.Single(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        static List<Stack<LockFileTargetLibrary>> GetChains(LockFileTarget target)
        {
            List<Stack<LockFileTargetLibrary>> chains = new List<Stack<LockFileTargetLibrary>>();

            var roots = target.Libraries.Where(e => GetParents(target, e).Count == 0).ToList();

            foreach (var root in roots)
            {
                var stack = new Stack<LockFileTargetLibrary>();
                stack.Push(root);

                chains.AddRange(GetChains(target, stack));
            }

            return chains;
        }

        static List<Stack<LockFileTargetLibrary>> GetChains(LockFileTarget target, Stack<LockFileTargetLibrary> chain)
        {
            List<Stack<LockFileTargetLibrary>> chains = new List<Stack<LockFileTargetLibrary>>();

            var deps = chain.Peek().Dependencies;

            foreach (var dep in deps)
            {
                var clone = new Stack<LockFileTargetLibrary>(chain.Reverse());
                clone.Push(GetId(target, dep.Id));

                chains.AddRange(GetChains(target, clone));
            }

            chains.Add(new Stack<LockFileTargetLibrary>(chain.Reverse()));

            return chains;
        }

        static List<Stack<LockFileTargetLibrary>> GetChainsWithId(List<Stack<LockFileTargetLibrary>> chains, string id)
        {
            return chains.Where(chain => chain.First().Name.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        static int GetDepthInChain(Stack<LockFileTargetLibrary> chain, string id)
        {
            var rev = chain.Reverse().ToList();

            for (int i=0; i < rev.Count; i++)
            {
                if (rev[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return Int32.MaxValue;
        }
    }
}