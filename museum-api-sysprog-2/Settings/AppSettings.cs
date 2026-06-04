namespace museum_api_sysprog_1.Settings;

public class AppSettings
{
    public int MaxCacheSize { get; set; }
    public int CacheTimeLimit { get; set; }
    public string MuseumApiKey { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public string GetListenerPrefix() => $"http://localhost:{Port}/";

    public string GetApiUrl(string query) =>
        $"https://collectionapi.metmuseum.org/public/collection/v1/search?{query}";
    public AppSettings()
    {
    }
}