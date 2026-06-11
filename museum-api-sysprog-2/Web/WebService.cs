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
                    else if (response.StatusCode==HttpStatusCode.NotFound)
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


            

        //   //kreira se kolekcija, tako da se za svaki task proveri LRU kes
        //   //kolekcija svih "poslova koji su zapoceti"
            IEnumerable<Task<Painting?>> fetchTasks = ids.Select(async id =>
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
                    
                //     }
                //    finally
                //    {
                //        localSemafor.Release();
                //    }
             

           });
        
            // Task.WhenAll ispaljuje svih 20 mreznih poziva odjednom, asinhrono
            //kako bi sacekali da se svi zapoceti "poslovi zavrse"
            Painting?[] results = await Task.WhenAll(fetchTasks);

            // filtriramo uspesno mapirane objekte 
            return results.Where(p => p != null).Cast<Painting>().ToList();

        }
    }

}