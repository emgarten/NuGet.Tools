using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CatalogIndex
{
    public static class HttpClientExtensions
    {
        public static Task<JObject> GetJObjectAsync(this HttpClient client, Uri address)
        {
            return client.GetJObjectAsync(address, CancellationToken.None);
        }

        public static Task<JObject> GetJObjectAsync(this HttpClient client, Uri address, CancellationToken token)
        {
            Task<string> task = client.GetStringAsync(address);
            return task.ContinueWith<JObject>((t) =>
            {
                try
                {
                    return JObject.Parse(t.Result);
                }
                catch (Exception e)
                {
                    throw new Exception(string.Format("GetJObjectAsync({0})", address), e);
                }
            });
        }

    }
}
