using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Xml.Linq;
using System.Globalization;

namespace NupkgLock
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine(".exe <nuspec name> <nupkg path>");
                Environment.Exit(1);
            }

            string nuspecName = args[0];

             var file = new FileInfo(args[1]);

             using (ZipArchive zip = new ZipArchive(file.Open(FileMode.Open, FileAccess.ReadWrite), ZipArchiveMode.Update))
             {
                 var nuspec = zip.GetEntry(nuspecName);

                 var data = nuspec.Open();

                 XDocument doc = XDocument.Load(data);

                 string ns = "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd";

                 var pkgNode = doc.Element(XName.Get("package", ns));

                 foreach (var dep in pkgNode.Element(XName.Get("metadata", ns)).Element(XName.Get("dependencies", ns)).Elements(XName.Get("dependency", ns)))
                 {
                     var versionAtt = dep.Attribute(XName.Get("version"));
                     var id = dep.Attribute(XName.Get("id")).Value;

                     if (versionAtt.Value.IndexOf('-') > -1 && versionAtt.Value.IndexOf(',') == -1)
                     {
                         string val = String.Format(CultureInfo.InvariantCulture, "[{0}]", versionAtt.Value);
                         Console.WriteLine("Locking {0} to {1}", id, val);
                         versionAtt.SetValue(val);
                     }
                     else
                     {
                         Console.WriteLine("Unlocked {0} {1}", id, versionAtt.Value);
                     }
                 }

                 data.Seek(0, SeekOrigin.Begin);

                 using (StreamWriter writer = new StreamWriter(data))
                 {
                     doc.Save(writer);
                 }
             }
        }
    }
}
