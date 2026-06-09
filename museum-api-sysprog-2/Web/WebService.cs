using System.Text.Json;
using System.Net.Http;
using System.Linq;
using System.Net;

namespace museum_api_sysprog_1.Web
{
    public class WebService : IWebService
    {
        private readonly Cache _cache;
        private readonly AppSettings _settings;
        private readonly HttpClient _httpClient = new();
        //ovo sam dodala da probam samo
        //sinhronizacija i kes stampedo kod taskova???
        private readonly ConcurrentDictionary<string, QueryCacheEntry> _queryCache = new();
        public WebService(AppSettings settings, Cache cache)
        {
            _cache = cache;
            _settings = settings;
            ServicePointManager.DefaultConnectionLimit = 100; // Dozvoljava vise mreznih izlaza odjednom
            //ovo je malo gusilo server
        }
        private string GenerateKey(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;
            string cleanQuery = query.TrimStart('?');
            string[] parts = cleanQuery.Split('&');
            Array.Sort(parts);
            return string.Join("&", parts).ToLower();
        }
        public List<Painting> GetPainting(string query)
        {
            var paintings = new List<Painting>();
            //isti zahtevi sa razlicitim redosledom parametara
            var key = GenerateKey(query);
            var queryEntry = _queryCache.GetOrAdd(key, _ => new QueryCacheEntry());

            List<int> ids = new List<int>();

            lock (queryEntry)
            {
                while (queryEntry.isLoading)
                {
                    Monitor.Wait(queryEntry);
                }

                if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
                {

                    ids = queryEntry.Ids;
                    Logger.Log("SERVER", $"Zahtev {query} je vec obradjen");
                }
                else
                {
                    queryEntry.isLoading = true;
                    try
                    {
                        var url = _settings.GetApiUrl(query);
                        var response = _httpClient.GetAsync(url).Result;

                        if (!response.IsSuccessStatusCode)
                            return paintings;

                        var searchJson = response.Content.ReadAsStringAsync().Result;
                        using (JsonDocument document = JsonDocument.Parse(searchJson))
                        {
                            if (document.RootElement.TryGetProperty("objectIDs", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                            {
                                ids = idsElement.EnumerateArray()
                                                .Take(20)
                                                .Select(x => x.GetInt32())
                                                .ToList();

                                queryEntry.Ids = ids;
                                queryEntry.Expiration = DateTime.Now.AddMinutes(5);
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        Logger.Log("SERVER", $"Greška pri dobavljanju ID-jeva: {ex.Message}");
                        return paintings;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("SERVER", $"Greška pri dobavljanju ID-jeva: {ex.Message}");
                        return paintings;
                    }
                    finally
                    {
                        queryEntry.isLoading = false;
                        Monitor.PulseAll(queryEntry);
                    }
                }
            }

            foreach (var id in ids)
            {
                var entry = _cache.GetOrCreateEntry(id);

                if (entry.IsLoading)
                {
                    lock (entry)
                    {
                        while (entry.IsLoading)
                        {
                            Monitor.Wait(entry);
                        }
                    }
                }

                if (entry.Data == null || !entry.IsValid)
                {
                    lock (entry)
                    {
                        if (entry.Data == null || !entry.IsValid)
                        {
                            try
                            {
                                entry.IsLoading = true;
                                var dataUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";
                                var dataResult = _httpClient.GetAsync(dataUrl).Result;

                                if (dataResult.IsSuccessStatusCode)
                                {
                                    var dataJson = dataResult.Content.ReadAsStringAsync().Result;
                                    entry.Data = JsonMapper.MapFromJson(dataJson);
                                    entry.TimeCreated = DateTime.Now;
                                    entry.IsValid = true;
                                }
                                else
                                {
                                    entry.IsValid = false;
                                    Logger.Log("SERVER", $"Server error id {id} status {dataResult.StatusCode}");
                                }
                            }
                            catch (Exception)
                            {
                                entry.IsValid = false;
                            }
                            finally
                            {
                                entry.IsLoading = false;
                                Monitor.PulseAll(entry);
                            }
                        }
                    }
                }
                else
                {
                    Logger.Log("Server", $"Podatak {id} je vec u kesu");
                }

                if (entry.Data is Painting p)
                {
                    paintings.Add(p);
                }
            }

            return paintings;
        }
    }
}