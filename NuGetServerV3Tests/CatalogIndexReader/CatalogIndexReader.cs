using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace CatalogIndex
{
    public class CatalogIndexReader
    {
        private readonly Uri _indexUri;
        private readonly HttpClient _httpClient;
        private JObject _context;

        public CatalogIndexReader(Uri indexUri)
            : this(indexUri, new HttpClient())
        {

        }

        public CatalogIndexReader(Uri indexUri, HttpClient httpClient)
        {
            _indexUri = indexUri;
            _httpClient = httpClient;
        }

        public JObject GetContext()
        {
            if (_context == null)
            {
                GetEntries().Wait();
            }

            return _context;
        }

        /// <summary>
        /// Returns only the latest id/version combination for each package. Older edits are skipped.
        /// </summary>
        public async Task<IEnumerable<CatalogIndexEntry>> GetRolledUpEntries()
        {
            HashSet<CatalogIndexEntry> set = new HashSet<CatalogIndexEntry>(CatalogIndexEntry.IdVersionComparer);

            var entries = await GetEntriesCommitTimeDesc();

            foreach (var entry in entries)
            {
                // ignore items we have already seen
                if (!set.Contains(entry))
                {
                    set.Add(entry);
                }
            }

            return set;
        }

        public async Task<IEnumerable<CatalogIndexEntry>> GetEntriesCommitTimeDesc()
        {
            var entries = await GetEntries();

            return entries.OrderByDescending(e => e.CommitTimeStamp);
        }

        public async Task<IEnumerable<CatalogIndexEntry>> GetEntries()
        {
            JObject index = await _httpClient.GetJObjectAsync(_indexUri);

            // save the context used on the index
            JToken context = null;
            if (index.TryGetValue("@context", out context))
            {
                _context = context as JObject;
            }

            List<Tuple<DateTime, Uri>> pages = new List<Tuple<DateTime, Uri>>();

            if (index["items"] != null)
            {
                foreach (var item in index["items"])
                {
                    pages.Add(new Tuple<DateTime, Uri>(DateTime.Parse(item["commitTimeStamp"].ToString()), new Uri(item["@id"].ToString())));
                }
            }
            else
            {
                Debug.Fail("invalid page: " + _indexUri.AbsoluteUri);
            }

            return GetEntries(pages.Select(p => p.Item2));
        }

        private ConcurrentBag<CatalogIndexEntry> GetEntries(IEnumerable<Uri> pageUris)
        {
            ConcurrentBag<CatalogIndexEntry> entries = new ConcurrentBag<CatalogIndexEntry>();

            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 32;

            Parallel.ForEach(pageUris.ToArray(), options, uri =>
            {
                var task = _httpClient.GetJObjectAsync(uri);
                task.Wait();

                JObject json = task.Result;

                foreach (var item in json["items"])
                {
                    var entry = new CatalogIndexEntry(new Uri(item["@id"].ToString()),
                            item["@type"].ToString(),
                            item["commitId"].ToString(),
                            DateTime.Parse(item["commitTimeStamp"].ToString()),
                            item["nuget:id"].ToString(),
                            NuGetVersion.Parse(item["nuget:version"].ToString()));

                    entries.Add(entry);
                }
            });

            return entries;
        }
    }
}
