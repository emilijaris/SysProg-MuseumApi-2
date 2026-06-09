public class QueryCacheEntry
{
    public List<int>? Ids { get; set; }
    // public bool isLoading { get; set; }
    public DateTime Expiration { get; set; }
    public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

}