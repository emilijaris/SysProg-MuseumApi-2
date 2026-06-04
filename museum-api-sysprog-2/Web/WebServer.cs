//jeca to do : 
using System.Net;
using System.Text;
using System.Text.Json;

namespace museum_api_sysprog_1.Web;

public class WebServer
{
    private readonly HttpListener _listener = new();

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
        Thread shutdownWatcher = new Thread(ListenForShutdown);
        shutdownWatcher.IsBackground = true;
        shutdownWatcher.Start();
        while (_isRunning)
        {
            try
            {
                // GetContext blokira nit dok klijent ne posalje zahtev
                var context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(HandleRequest, context);
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                //zbog listener-stop
                break;
            }
        }
        Logger.Log("SERVER", "Server je uspešno zaustavljen.");
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

    private void RespondWithJson(HttpListenerContext context, object content)
    {
        try
        {
            byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(content, new JsonSerializerOptions { WriteIndented = true });
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private void RespondWithText(HttpListenerContext context, string text)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Log("SERVER", $"Greška {ex.Message}");

        }
    }
}