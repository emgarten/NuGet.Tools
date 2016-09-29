using Microsoft.Deployment.Compression.Cab;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FindVSIXManifestNumber
{
    class Program
    {
        static void Main(string[] args)
        {
            DirectoryInfo dir = new DirectoryInfo(args[0]);
            DirectoryInfo temp = new DirectoryInfo(Environment.GetEnvironmentVariable("temp"));

            var files = dir.GetFiles("VS.Redist.Common.WPT.NuGet_VS14*");
            files = files.OrderByDescending(e => e.LastWriteTimeUtc).Take(20).ToArray();

            foreach (var file in files.OrderBy(e => e.LastWriteTimeUtc))
            {
                var root = new ZipArchive(file.OpenRead());

                var cabFile = root.GetEntry("nuget14_VisualStudio.cab");

                var cabStream = new MemoryStream();

                string cabPath = Path.Combine(temp.FullName, Guid.NewGuid() + ".tmp");
                var outStream = File.OpenWrite(cabPath);

                cabFile.Open().CopyTo(outStream, 4096);

                outStream.Close();

                CabInfo cab = new CabInfo(cabPath);

                using (var vsixStream = cab.OpenRead("NuGet.Tools.vsix"))
                {
                    ZipArchive vsixReader = new ZipArchive(vsixStream);

                    var manifestEntry = vsixReader.GetEntry("extension.vsixmanifest");

                    XDocument doc = XDocument.Load(manifestEntry.Open());

                    var identity = doc.Elements().First().Elements().First().Elements().First();

                    string version = identity.Attributes().FirstOrDefault(e => e.Name.LocalName == "Version").Value;

                    Console.WriteLine(version + " " +  file.FullName);
                }


                File.Delete(cabPath);
            }
        }
    }
}
