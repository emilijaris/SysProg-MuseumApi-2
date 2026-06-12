using System.Text.Json;
using System.Net.Http;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Linq.Expressions;

namespace museum_api_sysprog_1.Web
{
    public class WebService : IWebService
    {
        private readonly Cache _cache;
        private readonly AppSettings _settings;
        private readonly HttpClient _httpClient = new();
        //ovo sam dodala da probam samo
        private readonly ConcurrentDictionary<string, QueryCacheEntry> _queryCache = new();
        // Limitiramo isključivo broj istovremenih izlazaka na mrežu za pojedinačne slike
        private readonly SemaphoreSlim _networkSemaphore = new SemaphoreSlim(5, 5);
        //nesto sam pokusala sa ovim, nek ostane zakomentarisano, nisam nista postigla
        //dodato
        //jer Task.WaitAll salje sve zahteve odjednom i onda se Api buni
        // private readonly SemaphoreSlim localSemafor=new SemaphoreSlim(5,5);
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
        public async Task<List<Painting>?> GetPainting(string query)
        {
            var key = GenerateKey(query);
            var queryEntry = _queryCache.GetOrAdd(key, _ => new QueryCacheEntry());

            Task<List<int>> idsTask;

            if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
            {
                Logger.Log("SERVER", $"[HIT-LV1] Zahtev [{query}] nađen u kešu.");


                idsTask = Task.FromResult(queryEntry.Ids);
            }
            else
            {
                await queryEntry.Semaphore.WaitAsync();
                try
                {
                    if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
                    {
                        Logger.Log("SERVER", $"[LOCK HIT-LV1] ID-jevi uspešno uzeti iz keša nakon čekanja.");
                        idsTask = Task.FromResult(queryEntry.Ids);
                    }
                    else
                    {
                        Logger.Log("SERVER", $"[MISS-LV1] Direktan mrežni poziv za ID-jeve [{query}].");

                        var ids = new List<int>();
                        var url = _settings.GetApiUrl(query);

                        var response = await _httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var searchJson = await response.Content.ReadAsStringAsync();
                            using (JsonDocument document = JsonDocument.Parse(searchJson))
                            {
                                if (document.RootElement.TryGetProperty("objectIDs", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                                {
                                    ids = idsElement.EnumerateArray().Take(20).Select(x => x.GetInt32()).ToList();

                                    queryEntry.Ids = ids;
                                    queryEntry.Expiration = DateTime.Now.AddMinutes(2);
                                }
                            }
                        }
                        else if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            queryEntry.Ids = ids;
                            queryEntry.Expiration = DateTime.Now.AddMinutes(2);
                            Logger.Log("SERVER", $"[QUERY 404] Pojam [{query}] ne postoji.");
                        }

                        idsTask = Task.FromResult(ids);
                    }
                }
                finally
                {
                    queryEntry.Semaphore.Release();
                }
            }

            Task<List<Painting>> paintingsTask = idsTask.ContinueWith(async t =>
        {
            List<int> sviIdjevi = t.Result;
            List<Painting> skinuteSlike = new List<Painting>();

            Logger.Log("CONTINUEWITH", $"Nivo 1 završen. Jedan kontrolisani task započinje sekvencijalnu obradu za {sviIdjevi.Count} ID-jeva na Nivou 2.");


            foreach (int id in sviIdjevi)
            {
                Painting? p = await FetchPaintingAsync(id);
                if (p != null)
                {
                    skinuteSlike.Add(p);
                }
            }

            return skinuteSlike;

        }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();

            return await paintingsTask;
        }

        private async Task<Painting?> FetchPaintingAsync(int id)
        {
            var entry = _cache.GetOrCreateEntry(id);

            if (entry.Data != null && entry.IsValid)
                return entry.Data as Painting;

            await entry.Semaphore.WaitAsync();

            try
            {
                // Double-Check provera nakon što je nit dočekala svoj red i ušla u kritičnu sekciju
                if (entry.Data != null && entry.IsValid)
                {
                    Logger.Log("SERVER", $"[HIT-LV2] Slika {id} uzeta iz keša nakon čekanja na semaforu (Cache Stampede sprečen).");
                    return entry.Data as Painting;
                }

                Logger.Log("SERVER", $"[MISS-LV2] Pokreće se mrežni zahtev ka API-ju za sliku {id}.");
                var dataUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";

                var dataResult = await _httpClient.GetAsync(dataUrl);

                if (!dataResult.IsSuccessStatusCode)
                {
                    Logger.Log("SERVER_ERROR", $"Greška sa muzejskog API-ja za ID {id}: {dataResult.StatusCode}. Resetujem keš stavku.");
                    entry.Data = null;
                    entry.IsValid = false;
                    return null;
                }

                var dataJson = await dataResult.Content.ReadAsStringAsync();

                entry.Data = JsonMapper.MapFromJson(dataJson);
                entry.TimeCreated = DateTime.Now;
                entry.IsValid = true;

                return entry.Data as Painting;
            }
            catch (Exception ex)
            {
                Logger.Log("SERVER_EXCEPTION", $"Izuzetak za sliku {id}: {ex.Message}. Resetujem keš stavku.");
                entry.Data = null;
                entry.IsValid = false;
                return null;
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }


        /*
        ///verzija sa task when all 
        public async Task<List<Painting>?> GetPainting(string query)
        {
            var key = GenerateKey(query);
            var queryEntry = _queryCache.GetOrAdd(key, _ => new QueryCacheEntry());

            //double lock
            if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
            {
                Logger.Log("SERVER", $"[HIT-LV1] Zahtev [{query}] nađen u kešu.");

                //odmah vadimo iz kes-a
                var fetchTasks = queryEntry.Ids.Select(id => FetchPaintingAsync(id));
                //potencijalni problem jer ce pr 20 zahteva odjednom da posalje ka apiju pa dostigne se rate limt 
                var results = await Task.WhenAll(fetchTasks);
                return results.Where(p => p != null).Cast<Painting>().ToList();
            }

            await queryEntry.Semaphore.WaitAsync();
            Task<List<int>> idsTask;

            try
            {
                if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
                {
                    Logger.Log("SERVER", $"[LOCK HIT-LV1] ID-jevi uzeti iz kesa.");
                    idsTask = Task.FromResult(queryEntry.Ids);
                }
                else
                {
                    Logger.Log("SERVER", $"[MISS-LV1] Direktan mrežni poziv za ID-jeve [{query}].");

                    var ids = new List<int>();
                    var url = _settings.GetApiUrl(query);

                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var searchJson = await response.Content.ReadAsStringAsync();
                        using (JsonDocument document = JsonDocument.Parse(searchJson))
                        {
                            if (document.RootElement.TryGetProperty("objectIDs", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                            {
                                ids = idsElement.EnumerateArray().Take(20).Select(x => x.GetInt32()).ToList();
                                queryEntry.Ids = ids;
                                queryEntry.Expiration = DateTime.Now.AddMinutes(2);
                            }
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        queryEntry.Ids = ids;
                        queryEntry.Expiration = DateTime.Now.AddMinutes(2);
                        Logger.Log("SERVER", $"[QUERY 404] Pojam [{query}] ne postoji.");
                    }
                    //TODO: 
                    //todo: nije li ovo greska? 
                    idsTask = Task.FromResult(ids);
                }
            }
            finally
            {
                queryEntry.Semaphore.Release();
            }

            Task<List<Painting>> paintingsTask = idsTask.ContinueWith(async t =>
            {
                // t.Result nam daje kompletnu listu svih ID-jeva sa prvog nivoa
                List<int> sviIdjevi = t.Result;

                //Logger.Log("CONTINUEWITH", $"Nivo 1 završen. Prosleđujem svih {sviIdjevi.Count} ID-jeva na Nivo 2.");
                var sveSlikeTaskovi = sviIdjevi.Select(id => FetchPaintingAsync(id));
                //skupljamo da bi dali jednom task-u
                Painting?[] rezultatiSlika = await Task.WhenAll(sveSlikeTaskovi);
                return rezultatiSlika.Where(p => p != null).Cast<Painting>().ToList();

            }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
            // .Unwrap() skida ugnježdeni Task i spaja Nivo 1 i Nivo 2 u jedan linearan tok
            return await paintingsTask;
        }


        private async Task<Painting?> FetchPaintingAsync(int id)
        {
            var entry = _cache.GetOrCreateEntry(id);


            if (entry.Data != null && entry.IsValid)
                return entry.Data as Painting;

            await entry.Semaphore.WaitAsync();

            try
            {
                //double lock ovde
                if (entry.Data != null && entry.IsValid)
                {
                    Logger.Log("SERVER", $"[HIT-LV2] Slika {id} uzeta iz keša nakon čekanja.");
                    return entry.Data as Painting;
                }

                Logger.Log("SERVER", $"[MISS-LV2] Mrežni zahtev ka API-ju za sliku {id}.");
                var dataUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";

                await _networkSemaphore.WaitAsync();
                HttpResponseMessage dataResult;
                try
                {
                    //ako dodamo ogranicenje ovde na broj zahteva 
                    Logger.Log("SERVER", $"[MISS-LV2] Mrežni zahtev ka API-ju za sliku {id}.");
                    dataResult = await _httpClient.GetAsync(dataUrl);
                }
                finally
                {
                    // Čim HTTP zahtev završi (stigne odgovor), ODMAH oslobađamo mesto za sledeću sliku
                    _networkSemaphore.Release();
                }
                if (!dataResult.IsSuccessStatusCode)
                {
                    Logger.Log("SERVER_ERROR", $"Greška sa muzejskog API-ja za ID {id}: {dataResult.StatusCode}. Resetujem keš stavku.");
                    //ako je doslo do greske ovaj entri je nevalidan
                    entry.Data = null;
                    entry.IsValid = false;
                    return null;
                }

                var dataJson = await dataResult.Content.ReadAsStringAsync();

                entry.Data = JsonMapper.MapFromJson(dataJson);
                entry.TimeCreated = DateTime.Now;
                entry.IsValid = true;

                return entry.Data as Painting;
            }
            catch (Exception ex)
            {
                Logger.Log("SERVER_EXCEPTION", $"Izuzetak za sliku {id}: {ex.Message}. Resetujem keš stavku.");
                //ako imamo problem sa serverom takodje postavljamo entri da nije validna
                entry.Data = null;
                entry.IsValid = false;
                return null;
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }*/


        //todo: ovo ispod bi mogle da obrisemo : 
        //Zakomentarisala sam ono sto smo imale i ono sto sam prepravljala jer sam se pogubila
        /*
        public async Task<List<Painting>?> GetPainting(string query)
        {
            var paintings = new List<Painting>();


            //isti zahtevi sa razlicitim redosledom parametara
            var key = GenerateKey(query);
            var queryEntry = _queryCache.GetOrAdd(key, _ => new QueryCacheEntry());
            List<int> ids = new List<int>();

            //samo jedna nit za ovaj konkretan deo ulazi unutra
            await queryEntry.Semaphore.WaitAsync();
            try
            {

                //ako je prvi nivo zahteva vec u kesu
                if (queryEntry.Ids != null && queryEntry.Expiration > DateTime.Now)
                {
                    ids = queryEntry.Ids;
                    Logger.Log("SERVER", $"[QUERY HIT] Zahtev [{query}] je već obradjen, uzimam ID-jeve iz kesa.");
                }
                else
                {
                    Logger.Log("SERVER", $"[QUERY MISS] Pokrece se zahtev za ID-jeve upita [{query}]");
                    var url = _settings.GetApiUrl(query);
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {

                        var searchJson = await response.Content.ReadAsStringAsync();
                        using (JsonDocument document = JsonDocument.Parse(searchJson))
                        {
                            if (document.RootElement.TryGetProperty("objectIDs", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                            {
                                ids = idsElement.EnumerateArray()
                                                .Take(20)
                                                .Select(x => x.GetInt32())
                                                .ToList();

                                queryEntry.Ids = ids;
                                queryEntry.Expiration = DateTime.Now.AddMinutes(2);
                            }
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        //pravimo praznu listu
                        ids = new List<int>();
                        Logger.Log("SERVER", $"[QUERY 404] Pojam [{query}] ne postoji na muzeju.");

                        queryEntry.Ids = ids;
                        queryEntry.Expiration = DateTime.Now.AddMinutes(2);


                    }
                }

            }
            catch (HttpRequestException ex)
            {
                Logger.Log("SERVER", $"Greska pri dobavljanju ID-jeva: {ex.Message}");
                return paintings;
            }
            catch (OperationCanceledException ex)
            {
                Logger.Log("SERVER", $"Greska pri dobavljanju ID-jeva: {ex.Message}");
                return paintings;
            }
            catch (Exception ex)
            {
                Logger.Log("SERVER", $"Greska pri dobavljanju ID-jeva: {ex.Message}");
                return paintings;
            }
            finally
            {
                //oslobadjanje semafora
                queryEntry.Semaphore.Release();
            }




            //kreira se kolekcija, tako da se za svaki task proveri LRU kes
            //kolekcija svih "poslova koji su zapoceti"
            //dodala sam metodu za id-eve tj za jedno nabavljanje slike 
            /* IEnumerable<Task<Painting?>> fetchTasks = ids.Select(async id =>
            {
                // uzimamo entry iz tvog LRU kesa
                var entry = _cache.GetOrCreateEntry(id);


                if (entry.Data != null && entry.IsValid)
                {
                    return entry.Data as Painting;
                }
                //resavamo problem istovremenih zahteva ka apiju
                //  await localSemafor.WaitAsync();
                //   try
                //    {

                // asinhrono zakljucavanje 
                await entry.Semaphore.WaitAsync();
                try
                {

                    if (entry.Data != null && entry.IsValid)
                    {
                        Logger.Log("SERVER", $"[HIT] Slika {id} uzeta iz kesa.");
                        return entry.Data as Painting;
                    }



                    var dataUrl = $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";
                    var dataResult = await _httpClient.GetAsync(dataUrl);

                    if (dataResult.IsSuccessStatusCode)
                    {
                        var dataJson = await dataResult.Content.ReadAsStringAsync();


                        entry.Data = JsonMapper.MapFromJson(dataJson);
                        entry.TimeCreated = DateTime.Now;
                        entry.IsValid = true;

                        return entry.Data as Painting;
                    }
                    else
                    {
                        entry.IsValid = false;
                        Logger.Log("SERVER", $"Greska pri povlacenju ID-ja {id}: {dataResult.StatusCode}");
                        return null;
                    }

                }
                catch (Exception ex)
                {
                    entry.IsValid = false;
                    Logger.Log("SERVER", $"Izuzetak za ID {id}: {ex.Message}");
                    return null;
                }
                finally
                {
                    entry.Semaphore.Release(); // oslobadjamo semafor 
                }
            });


            Task<List<Painting>> paintingsTask =
                Task.Run(() => ids)
                    .ContinueWith(async t =>
                    {
                        List<Painting> paintings = new();

                        foreach (int id in t.Result)
                        {
                            //za svaki id se poziva fetch painting
                            var painting = await FetchPaintingAsync(id);

                            if (painting != null)
                                paintings.Add(painting);
                        }

                        return paintings;

                    }, TaskContinuationOptions.OnlyOnRanToCompletion)
                    .Unwrap();

            return await paintingsTask;

        }

        private async Task<Painting?> FetchPaintingAsync(int id)
        {
            var entry = _cache.GetOrCreateEntry(id);

            if (entry.Data != null && entry.IsValid)
                return entry.Data as Painting;

            await entry.Semaphore.WaitAsync();

            try
            {
                if (entry.Data != null && entry.IsValid)
                {
                    Logger.Log("SERVER", $"[HIT] Slika {id} uzeta iz kesa.");
                    return entry.Data as Painting;
                }

                var dataUrl =
                    $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}";

                var dataResult = await _httpClient.GetAsync(dataUrl);

                if (!dataResult.IsSuccessStatusCode)
                {
                    entry.IsValid = false;
                    return null;
                }

                var dataJson = await dataResult.Content.ReadAsStringAsync();

                entry.Data = JsonMapper.MapFromJson(dataJson);
                entry.TimeCreated = DateTime.Now;
                entry.IsValid = true;

                return entry.Data as Painting;
            }
            catch
            {
                entry.IsValid = false;
                return null;
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }
    }*/
    }

}