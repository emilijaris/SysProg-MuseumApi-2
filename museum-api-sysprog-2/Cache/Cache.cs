using System.Linq;
namespace museum_api_sysprog_1.CacheStructure;

public class Cache
{
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private readonly object _lruLock = new();
    private readonly LinkedList<int> _accessOrder = new();
    public Cache(AppSettings s)
    {
        _settings = s;
    }
    public void StartCleanupThread()
    {
        Thread cleaner = new Thread(CleanupLoop);
        cleaner.IsBackground = true;
        cleaner.Name = "CacheJanitor";
        cleaner.Start();
    }

    private void CleanupLoop()
    {
        while (true)
        {
            Thread.Sleep(10000);
            if (_cache.IsEmpty)
                continue;
            Logger.Log("JANITOR", "Provera isteka stavki...");
            var zaBrisanje = _cache.Where(kvp =>
                !kvp.Value.IsLoading &&
                (DateTime.Now - kvp.Value.TimeCreated).TotalSeconds > _settings.CacheTimeLimit)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in zaBrisanje)
            {
                // TryRemove osigurava da samo jedna nit (Janitor) zapravo
                if (_cache.TryRemove(key, out _))
                {
                    lock (_lruLock)
                    {
                        _accessOrder.Remove(key);
                    }
                    Logger.Log("JANITOR", $"Obrisan ID {key}");
                }
            }
        }
    }

    public CacheEntry GetOrCreateEntry(int key)
    {
        bool isNew = false;
        var entry = _cache.GetOrAdd(key, k =>
        {
            isNew = true;
            return new CacheEntry(); // IsLoading je ovde false
        });

        if (isNew)
        {
            lock (_lruLock)
            {
                if (_cache.Count > _settings.MaxCacheSize)
                    RemoveLRU();
                _accessOrder.AddLast(key);
            }
            Logger.Log("KEŠ", $"Stavka {key} je dodata");
        }
        else
        {
            // Provera validnosti za postojece stavke
            if (entry.Data != null)
            {
                double starost = (DateTime.Now - entry.TimeCreated).TotalSeconds;
                if (starost > _settings.CacheTimeLimit)
                {
                    entry.IsValid = false;
                }
            }
            // Update LRU samo ako nije u toku ucitavanje
            if (!entry.IsLoading)
                UpdateLRU(key);
        }
        return entry;
    }
    private void UpdateLRU(int key)
    {
        lock (_lruLock)
        {
            //ako ne postoji, nista nece da se desi 
            if (_accessOrder.Remove(key))
            {
                _accessOrder.AddLast(key);
            }
        }
    }

    private void RemoveLRU()
    {
        if (_accessOrder.First != null)
        {
            int najstariji = _accessOrder.First.Value;
            _accessOrder.RemoveFirst();
            _cache.TryRemove(najstariji, out _);
            Logger.Log("KEŠ", $"Uklonjen ID {najstariji}");
        }
    }
}
