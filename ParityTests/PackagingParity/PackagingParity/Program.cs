using NuGet.Frameworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace PackagingParity
{
    public class Program
    {
        static void Main(string[] args)
        {
            var possibleFrameworks = GetPossibleFrameworks();

            using (StreamWriter writer = new StreamWriter("parse-report.csv", false))
            {
                writer.WriteLine("folder,legacy,current");

                foreach (string folderName in possibleFrameworks)
                {
                    NuGetFramework fw = NuGetFramework.Parse(folderName);
                    FrameworkName newFw = new FrameworkName(fw.DotNetFrameworkName);

                    FrameworkName legacyFw = null;

                    try
                    {
                        legacyFw = NuGet.VersionUtility.ParseFrameworkName(folderName) ?? new FrameworkName("Unsupported", new Version(0, 0));

                        if (legacyFw.Identifier == ".NETPortable")
                        {
                            var portable = NuGet.NetPortableProfile.Parse(legacyFw.Profile, false);
                            legacyFw = new FrameworkName(legacyFw.Identifier, legacyFw.Version, portable.Name);
                        }
                    }
                    catch
                    {
                        legacyFw = new FrameworkName("Unsupported", new Version(0, 0));
                    }

                    if (!Equals(legacyFw, fw))
                    {
                        writer.WriteLine("{0},{1},{2}", folderName, legacyFw.FullName.Replace(',', ' '), newFw.FullName.Replace(',', ' '));
                    }
                }
            }
        }

        private static bool Equals(FrameworkName legacyFramework, NuGetFramework packagingFramework)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(legacyFramework.Identifier, packagingFramework.Framework)
                && NormalizeVersion(legacyFramework.Version).Equals(NormalizeVersion(packagingFramework.Version))
                && StringComparer.OrdinalIgnoreCase.Equals(NormalizeProfile(legacyFramework.Profile), NormalizeProfile(packagingFramework.Profile));
        }

        private static string NormalizeProfile(string profile)
        {
            if (String.IsNullOrEmpty(profile))
            {
                return string.Empty;
            }

            return profile;
        }

        private static Version NormalizeVersion(Version version)
        {
            return new Version(Math.Max(version.Major, 0),
                               Math.Max(version.Minor, 0),
                               Math.Max(version.Build, 0),
                               Math.Max(version.Revision, 0));
        }

        private static IEnumerable<string> GetPossibleFrameworks()
        {
            HashSet<string> possibleFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (StreamReader reader = new StreamReader("possibleframeworks.txt"))
            {
                while (!reader.EndOfStream)
                {
                    string s = reader.ReadLine().Trim();

                    if (!String.IsNullOrEmpty(s))
                    {
                        possibleFrameworks.Add(s);
                    }
                }
            }

            return possibleFrameworks;
        }
    }
}
