using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using NuGet.ProjectModel;

namespace MakeRelease
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("makerelease.exe <dir>");
                return;
            }

            var format = new LockFileFormat();

            var dir = new DirectoryInfo(args[0]);

            if (dir.Exists)
            {
                foreach (var file in dir.GetFiles("project.json", SearchOption.AllDirectories))
                {
                    Console.WriteLine(file.FullName);

                    var lockFilePath = Path.Combine(file.DirectoryName, "project.lock.json");

                    if (File.Exists(lockFilePath))
                    {
                        var lockFile = format.Read(lockFilePath);

                        JObject json = null;
                        bool changes = false;

                        using (var reader = file.OpenText())
                        {
                            json = JObject.Parse(reader.ReadToEnd());

                            foreach (var node in json.Descendants())
                            {
                                //if (node.Parent.Type == JTokenType.Property)
                                //{
                                //    changes |= RemovePreleaseLabel(node);
                                //}

                                if (node.Type == JTokenType.Property)
                                {
                                    changes |= ReplaceWithLockFileVersion((JProperty)node, lockFile);
                                }
                            }
                        }

                        if (changes)
                        {
                            file.Delete();

                            using (var writer = new StreamWriter(file.OpenWrite(), Encoding.UTF8))
                            {
                                writer.Write(json.ToString());
                            }
                        }
                    }
                }
            }
        }

        static bool RemovePreleaseLabel(JToken token)
        {
            try
            {
                var value = token as JValue;

                if (value != null)
                {
                    var version = value.Value.ToString();

                    var range = VersionRange.Parse(version);

                    if (range.IsFloating && ((range.Float.MinVersion.Major == 3 && range.Float.MinVersion.Minor == 2)
                        || (range.Float.MinVersion.Major == 2 && range.Float.MinVersion.Minor == 8
                        && range.Float.MinVersion.Patch == 8)))
                    {
                        value.Value = version.Split('-').First();

                        Console.WriteLine("{0} -> {1}", version, value.ToString());

                        return true;
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        static bool ReplaceWithLockFileVersion(JProperty dependencyEntry, LockFile lockFile)
        {
            if (dependencyEntry.Value.Type == JTokenType.String && dependencyEntry.Name != "version")
            {
                var version = dependencyEntry.Value.ToString();

                if (version.EndsWith("*") && !dependencyEntry.Name.StartsWith("NuGet", StringComparison.OrdinalIgnoreCase))
                {
                    var lib = lockFile.Libraries.SingleOrDefault(package =>
                        package.Name.Equals(
                            dependencyEntry.Name,
                            StringComparison.OrdinalIgnoreCase));

                    if (lib == null)
                    {
                        Console.WriteLine("Missing: " + dependencyEntry.Name);
                    }
                    else
                    {
                        dependencyEntry.Value = new JRaw("\"" + lib.Version.ToNormalizedString() + "\"");
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
