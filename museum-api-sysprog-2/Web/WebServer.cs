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
    //channel promeni 
    private readonly BlockingCollection<HttpListenerContext> _requestQueue = new();
    //dodala sam ovde (treba prebaciti u settings)
    private readonly int _maxWorkerTasks = 12;
    private readonly List<Task> _workerTasks = new();
    private readonly WebService _webService;
    private readonly AppSettings _settings;
    // private volatile bool _isRunning = true;

    private readonly CancellationTokenSource _cTokenSource = new CancellationTokenSource();

    public WebServer(AppSettings settings, WebService webService)
    {
        _settings = settings;
        _webService = webService;
        _listener.Prefixes.Add(_settings.GetListenerPrefix());
    }

    //jel treba ovo uopste mislim na async za start????
    public async Task Start()
    {
        _listener.Start();
        Logger.Log("SERVER", $"Web server pokrenut na {_settings.GetListenerPrefix()}");
        //rekle smo ostaje nit a pogledacemo cancelation tokene

        // Thread shutdownWatcher = new Thread(ListenForShutdown);
        // shutdownWatcher.IsBackground = true;
        //shutdownWatcher.Start();
        //Jel ovo ok ?, jer  shutdowntask vec vraca task? 
        var shutdownTask = ListenForShutdown();


        for (int i = 0; i < _maxWorkerTasks; i++)
        {
            //TODO: 
            //todo: jel i ovde taskrun treba da sklonimo jer je processqueueasync vec async
            //todo: mislim da da ali nisam sigurna zato sam ostavila
            _workerTasks.Add(Task.Run(() => ProcessQueueAsync(_cTokenSource.Token)));
        }
        while (!_cTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                //da li je potrebno ovde jedno await i da getcontext promenimo u getcontextasync???
                //samo da nit ne bi cekala i bila blokirana dok slusa nego da se vrrati u tp
                var context = await _listener.GetContextAsync();
                if (!_cTokenSource.Token.IsCancellationRequested)
                    _requestQueue.Add(context);
            }
            catch (HttpListenerException) when (_cTokenSource.Token.IsCancellationRequested)
            {
                //zbog listener-stop
                break;
            }
        }
        _requestQueue.CompleteAdding();
        //cekamo da se zavrse zahtevi koji su vec bili u redu
        Task.WaitAll(_workerTasks.ToArray());

        _requestQueue.Dispose();
        _cTokenSource.Dispose();
        Logger.Log("SERVER", "Server je uspesno zaustavljen.");
    }
    private async Task ProcessQueueAsync(CancellationToken token)
    {
        foreach (var context in _requestQueue.GetConsumingEnumerable())
        {
            if (token.IsCancellationRequested)
                break;
            //ovo prepraviti 
            //await Task.Run(() => HandleRequest(context));
            await HandleRequest(context);
        }
    }
    async private Task HandleRequest(object? state)
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
                await RespondWithJsonAsync(context, new List<Painting>());
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
            var paintings = await _webService.GetPainting(fullQuery);
            // sw.Stop();
            // Logger.Log("TIMER", $"Zahtev {fullQuery} obradjen za {sw.ElapsedMilliseconds}");



            if (paintings == null || paintings!.Count == 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await RespondWithTextAsync(context, $"GRESKA: Umetnicko delo za upit {fullQuery} nije pronadjeno!");
                return;
            }

            //neka sitna ideja za koriscenje ContinueWith
            Task responseTask = RespondWithJsonAsync(context, paintings);
            await responseTask.ContinueWith(t =>
            {
                sw.Stop();
                Logger.Log("TIMER", $"Zahtev {fullQuery} obradjen za {sw.ElapsedMilliseconds}");

                if (t.IsFaulted)
                {
                    Logger.Log("SERVER_ERROR", $"Prekinuta veza pre uspesnog primljenog odgovora");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        catch (MuseumException ex)
        {
            context.Response.StatusCode = ex.StatusCode != 0 ? ex.StatusCode : 500;
            await RespondWithTextAsync(context, ex.ApiMessage ?? ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Log("SERVER", $"Zahtev prekinut usled gašenja servera: {ex.Message}");
            await RespondWithTextAsync(context, "Server se gasi. Zahtev je otkazan.");
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await RespondWithTextAsync(context, "Interna greska servera:  " + ex.Message);

        }

    }

    private async Task ListenForShutdown()
    {
        Logger.Log("SERVER", "Pritisnite 'Q' za Graceful Shutdown.");
        while (!_cTokenSource.Token.IsCancellationRequested)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
            {
                // _isRunning = false;
                _cTokenSource.Cancel();
                _listener.Stop(); // Ovo prekida _listener.GetContext() 
                break;
            }
            // Thread.Sleep(200); // Da ne opterecujemo procesor
            await Task.Delay(200, _cTokenSource.Token);
        }
    }

    private async Task RespondWithJsonAsync(HttpListenerContext context, object content)
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

    private async Task RespondWithTextAsync(HttpListenerContext context, string text)
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