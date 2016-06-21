using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using CatalogIndex;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace PackagesConfigGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            var count = 500;

            CatalogIndexReader reader = new CatalogIndexReader(new Uri("https://api.nuget.org/v3/catalog0/index.json"));

            var entries = reader.GetRolledUpEntries().Result;
            var packages = entries.Select(e => new PackageIdentity(e.Id, e.Version)).ToList();

            Random rand = new Random();

            HashSet<PackageIdentity> selected = new HashSet<PackageIdentity>();

            HttpClient http = new HttpClient();

            var doc = new XDocument();
            var root = new XElement(XName.Get("packages"));
            doc.Add(root);

            var expectedPackages = new HashSet<PackageIdentity>();
            var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (selected.Count < count)
            {
                var package = packages[rand.Next(0, packages.Count - 1)];

                if (package.Id.IndexOf("-") < 0 && selected.Add(package) && allIds.Add(package.Id))
                {
                    var nuspecUrl = $"https://api.nuget.org/v3-flatcontainer/{package.Id.ToLowerInvariant()}/{package.Version.ToNormalizedString().ToLowerInvariant()}/{package.Id.ToLowerInvariant()}.nuspec";

                    var response = http.GetStringAsync(nuspecUrl).Result;
                    var nuspec = new NuspecReader(XDocument.Parse(response));
                    var fromNuspec = nuspec.GetIdentity();

                    var entry = new XElement(XName.Get("package"));
                    entry.Add(new XAttribute(XName.Get("id"), fromNuspec.Id));
                    entry.Add(new XAttribute(XName.Get("version"), fromNuspec.Version.ToString()));

                    root.Add(entry);
                }
            }

            string name = string.Format("packages.config");

            using (var writer = new StreamWriter(name))
            {
                writer.WriteLine(doc.ToString());
            }
        }
    }
}
