using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace NuGet.CatalogReader
{
    /// <summary>
    /// NuGet v3 catalog reader.
    /// </summary>
    public class CatalogReader
    {
        private readonly Uri _indexUri;
        private readonly HttpClient _httpClient;
        private const int MaxThreads = 16;

        /// <summary>
        /// CatalogReader
        /// </summary>
        /// <param name="indexUri">URI of the catalog service.</param>
        public CatalogReader(Uri indexUri)
            : this(indexUri, new HttpClient())
        {
        }

        /// <summary>
        /// CatalogReader
        /// </summary>
        /// <param name="indexUri">URI of the catalog service.</param>
        /// <param name="httpClient">HTTP client</param>
        public CatalogReader(Uri indexUri, HttpClient httpClient)
        {
            _indexUri = indexUri;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Returns only the latest id/version combination for each package. Older edits are ignored.
        /// </summary>
        public Task<IReadOnlyList<CatalogEntry>> GetRolledUpEntries(CancellationToken token)
        {
            return GetRolledUpEntries(DateTimeOffset.MinValue, DateTimeOffset.Now, token);
        }

        /// <summary>
        /// Returns only the latest id/version combination for each package. Older edits are ignored.
        /// </summary>
        /// <param name="start">End time of the previous window. Commits exactly matching the start time will NOT be included. This is designed to take the cursor time.</param>
        /// <param name="end">Maximum time to include. Exact matches will be included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries within the start and end time. Start time is NOT included.</returns>
        public async Task<IReadOnlyList<CatalogEntry>> GetRolledUpEntries(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            var set = new HashSet<CatalogEntry>();

            var entries = await GetEntriesCommitTimeDesc(start, end, token);

            foreach (var entry in entries)
            {
                // ignore items we have already seen
                set.Add(entry);
            }

            return set.OrderByDescending(e => e.CommitTimeStamp).ToList();
        }

        /// <summary>
        /// Returns an index of all packages in the catalog.
        /// Id -> Version set.
        /// </summary>
        public Task<IDictionary<string, ISet<NuGetVersion>>> GetPackageSet(CancellationToken token)
        {
            return GetPackageSet(DateTimeOffset.MinValue, DateTimeOffset.Now, token);
        }

        /// <summary>
        /// Returns an index of all packages in the catalog.
        /// Id -> Version set.
        /// </summary>
        /// <param name="start">End time of the previous window. Commits exactly matching the start time will NOT be included. This is designed to take the cursor time.</param>
        /// <param name="end">Maximum time to include. Exact matches will be included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries within the start and end time. Start time is NOT included.</returns>
        public async Task<IDictionary<string, ISet<NuGetVersion>>> GetPackageSet(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            var packages = new Dictionary<string, ISet<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in await GetRolledUpEntries(start, end, token))
            {
                ISet<NuGetVersion> versions;
                if (!packages.TryGetValue(entry.Id, out versions))
                {
                    versions = new SortedSet<NuGetVersion>();
                    packages.Add(entry.Id, versions);
                }

                versions.Add(entry.Version);
            }

            return packages;
        }

        /// <summary>
        /// Read all catalog entries.
        /// </summary>
        public Task<IReadOnlyList<CatalogEntry>> GetEntries(CancellationToken token)
        {
            return GetEntries(DateTimeOffset.MinValue, DateTimeOffset.Now, token);
        }

        /// <summary>
        /// Read all catalog entries.
        /// </summary>
        /// <param name="start">End time of the previous window. Commits exactly matching the start time will NOT be included. This is designed to take the cursor time.</param>
        /// <param name="end">Maximum time to include. Exact matches will be included.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries within the start and end time. Start time is NOT included.</returns>
        public async Task<IReadOnlyList<CatalogEntry>> GetEntries(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            var index = await GetJObject(_indexUri);

            var pages = new List<Tuple<DateTimeOffset, Uri>>();

            if (index["items"] != null)
            {
                foreach (var item in index["items"])
                {
                    var dateTimeString = item.Value<string>("commitTimeStamp");
                    var dateTime = DateTimeOffset.Parse(dateTimeString);
                    var pageUri = new Uri(item["@id"].ToString());

                    pages.Add(new Tuple<DateTimeOffset, Uri>(dateTime, pageUri));
                }
            }
            else
            {
                Debug.Fail("invalid page: " + _indexUri.AbsoluteUri);
            }

            // Take all commits in the range specified
            // Also take the pages before and after, these may include commits within the specified range also.
            var commitsInRange = pages.Where(p => p.Item1 <= end && p.Item1 > start);
            var commitAfter = pages.Where(p => p.Item1 > end).OrderBy(p => p.Item1).FirstOrDefault();
            var commitBefore = pages.Where(p => p.Item1 <= start).OrderByDescending(p => p.Item1).FirstOrDefault();

            var uris = new HashSet<Uri>();

            if (commitAfter != null)
            {
                uris.Add(commitAfter.Item2);
            }

            if (commitBefore != null)
            {
                uris.Add(commitBefore.Item2);
            }

            uris.UnionWith(commitsInRange.Select(p => p.Item2));

            var entries = await GetEntries(uris);

            return entries.Where(e => e.CommitTimeStamp > start && e.CommitTimeStamp <= end).ToList();
        }

        private async Task<IReadOnlyList<CatalogEntry>> GetEntriesCommitTimeDesc(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            var entries = await GetEntries(start, end, token);

            return entries.OrderByDescending(e => e.CommitTimeStamp).ToList();
        }

        private async Task<List<CatalogEntry>> GetEntries(IEnumerable<Uri> pageUris)
        {
            var entries = new List<CatalogEntry>();
            var tasks = new List<Task<JObject>>(MaxThreads);

            foreach (var uri in pageUris)
            {
                while (tasks.Count > MaxThreads)
                {
                    entries.AddRange(await CompleteTask(tasks));
                }

                var task = GetJObject(uri);
                tasks.Add(task);
            }

            while (tasks.Count > 0)
            {
                entries.AddRange(await CompleteTask(tasks));
            }

            return entries;
        }

        private async Task<List<CatalogEntry>> CompleteTask(List<Task<JObject>> tasks)
        {
            var entries = new List<CatalogEntry>();

            if (tasks.Count > 0)
            {
                var task = await Task.WhenAny(tasks);
                var json = await task;
                tasks.Remove(task);

                entries.AddRange(GetEntriesFromJson(json));
            }

            return entries;
        }

        private static List<CatalogEntry> GetEntriesFromJson(JObject json)
        {
            var entries = new List<CatalogEntry>();

            foreach (var item in json["items"])
            {
                var entry = new CatalogEntry(new Uri(item["@id"].ToString()),
                        item["@type"].ToString(),
                        item["commitId"].ToString(),
                        DateTime.Parse(item["commitTimeStamp"].ToString()),
                        item["nuget:id"].ToString(),
                        NuGetVersion.Parse(item["nuget:version"].ToString()));

                entries.Add(entry);
            }

            return entries;
        }

        private async Task<JObject> GetJObject(Uri uri)
        {
            using (var stream = await _httpClient.GetStreamAsync(uri))
            using (var streamReader = new StreamReader(stream))
            using (var reader = new JsonTextReader(streamReader))
            {
                // Avoid error prone json.net date handling
                reader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(reader);
            }
        }
    }
}
