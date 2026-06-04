using System.Security.AccessControl;

namespace museum_api_sysprog_1.CacheStructure;

public class CacheEntry
{
    public Painting? Data { get; set; }
    public bool IsLoading { get; set; } = false;
    //vreme kreiranja stavke u kesu
    public DateTime TimeCreated { get; set; } = DateTime.Now;
    public bool IsValid { get; set; } = true;

}