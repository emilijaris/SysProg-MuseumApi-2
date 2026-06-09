//jeca to do : 
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace museum_api_sysprog_1.Web;

public class WebServer
{
    private readonly HttpListener _listener = new();
    //dodala sam blocking collection 
    //zasto? 
    //jer : 
    /*
    .Add(item) probudice se jedan radnik/task i preuzeti ovo 
    .GetConsumingEnumerable() – svi raspolozivi taskovi pokrecu petlju i proveravaju da li postoji taskova za rad
    */
    private readonly BlockingCollection<HttpListenerContext> _requestQueue = new();
    //dodala sam ovde (treba prebaciti u settings)
    private readonly int _maxWorkerTasks = 4;
    private readonly List<Task> _workerTasks = new();
    private readonly WebService _webService;
    private readonly AppSettings _settings;
    private volatile bool _isRunning = true;

    public WebServer(AppSettings settings, WebService webService)
    {
        _settings = settings;
        _webService = webService;
        _listener.Prefixes.Add(_settings.GetListenerPrefix());
    }

    public void Start()
    {
        _listener.Start();
        Logger.Log("SERVER", $"Web server pokrenut na {_settings.GetListenerPrefix()}");
        //rekle smo ostaje nit a pogledacemo cancelation tokene

        Thread shutdownWatcher = new Thread(ListenForShutdown);
        shutdownWatcher.IsBackground = true;
        shutdownWatcher.Start();

        for (int i = 0; i < _maxWorkerTasks; i++)
        {
            _workerTasks.Add(Task.Run(ProcessQueueAsync));
        }
        while (_isRunning)
        {
            try
            {
                // GetContext blokira nit dok klijent ne posalje zahtev
                var context = _listener.GetContext();
                _requestQueue.Add(context);
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                //zbog listener-stop
                break;
            }
        }
        _requestQueue.CompleteAdding();
        Task.WaitAll(_workerTasks.ToArray());
        Logger.Log("SERVER", "Server je uspešno zaustavljen.");
    }
    private async Task ProcessQueueAsync()
    {
        foreach (var context in _requestQueue.GetConsumingEnumerable())
        {
            await Task.Run(() => HandleRequest(context));
            //gde treba continue with da dodamo? 

        }
    }
    private void HandleRequest(object? state)
    {
        var context = (HttpListenerContext)state!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            //geenerisanje query stringa od svih parametara koje je klijent poslao
            var queryParams = context.Request.QueryString;
            if (queryParams.Count == 0)
            {
                //to je zbog one dosadne ikonice
                //ako nema parametara (da ne bi konstanto bio log sa praznim parametrima)
                RespondWithJson(context, new List<Painting>());
                return;
            }
            var builder = new StringBuilder();

            foreach (string key in queryParams.AllKeys)
            {
                if (builder.Length > 0)
                    builder.Append("&");
                builder.Append($"{key}={Uri.EscapeDataString(queryParams[key]!)}");
            }
            string fullQuery = builder.ToString();
            Logger.Log("SERVER", $"Obrada zahteva: {fullQuery}");

            //dodato u slucajo da ne postoji umetnicko delo
            var paintings = _webService.GetPainting(fullQuery);
            sw.Stop();
            Logger.Log("TIMER", $"Zahtev {fullQuery} obradjen za {sw.ElapsedMilliseconds}");

            if (paintings == null || paintings!.Count == 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                RespondWithText(context, $"GRESKA: Umetnicko delo za upit {fullQuery} nije pronadjeno!");
                return;
            }
            RespondWithJson(context, paintings);
        }
        catch (MuseumException ex)
        {
            context.Response.StatusCode = ex.StatusCode != 0 ? ex.StatusCode : 500;
            RespondWithText(context, ex.ApiMessage ?? ex.Message);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            RespondWithText(context, "Interna greska servera:  " + ex.Message);

        }

    }

    private void ListenForShutdown()
    {
        Logger.Log("SERVER", "Pritisnite 'Q' za Graceful Shutdown.");
        while (_isRunning)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
            {
                _isRunning = false;
                _listener.Stop(); // Ovo prekida _listener.GetContext() 
                break;
            }
            Thread.Sleep(200); // Da ne opterecujemo procesor
        }
    }

    private async Task RespondWithJson(HttpListenerContext context, object content)
    {
        try
        {
            byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(content, new JsonSerializerOptions { WriteIndented = true });
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private async Task RespondWithText(HttpListenerContext context, string text)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Log("SERVER", $"Greška {ex.Message}");

        }
    }
}