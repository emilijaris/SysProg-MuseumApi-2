using System.Threading.Tasks;

namespace museum_api_sysprog_1;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MUSEUM API SERVER STARTUP ===");
        try
        {
            var settings = new AppSettings
            {
                Port = 8080,
                MaxCacheSize = 100,
                CacheTimeLimit = 120 //2min je stavka validna
            };

            Cache cache = new Cache(settings);
            cache.StartCleanupThread();
            WebService webService = new WebService(settings, cache);
            WebServer server = new WebServer(settings, webService);
            await server.Start();
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"KRITIČNA GREŠKA PRI POKRETANJU: {ex.Message}");
            Console.ResetColor();
        }
        Console.WriteLine("Aplikacija je ugašena.");
    }
}