using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NuGetVSTemplateFix
{
    class Program
    {
        private const string find = "NuGet.VisualStudio.Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
        private const string replace = "NuGet.VisualStudio.Interop";

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine(".exe <root path>");
                System.Environment.Exit(1);
            }

            DirectoryInfo dir = new DirectoryInfo(args[0]);

            foreach(var file in dir.EnumerateFiles("*.vstemplate", SearchOption.AllDirectories))
            {
                try
                {
                    XDocument xml = null;
                    bool save = false;

                    using (var stream = file.OpenRead())
                    {
                        xml = XDocument.Load(stream);

                        var ns = xml.Descendants().FirstOrDefault().GetDefaultNamespace().NamespaceName;

                        var node = xml.Descendants(XName.Get("VSTemplate", ns)).Descendants(XName.Get("WizardExtension", ns)).Descendants(XName.Get("Assembly", ns)).FirstOrDefault();

                        if (node != null)
                        {
                            if (node.Value == find)
                            {
                                node.SetValue(replace);
                                save = true;

                                Console.WriteLine("Updating: " + file.FullName);
                            }
                        }
                    }

                    if (save)
                    {
                        xml.Save(file.FullName);
                    }
                }
                catch
                {
                    // Ignore exceptions
                }
            }
        }
    }
}
