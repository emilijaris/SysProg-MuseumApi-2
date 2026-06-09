using System.Threading.Tasks;

namespace museum_api_sysprog_1.Interfaces
{
    public interface IWebService
    {
        Task<List<Painting>?> GetPainting(string query);
    }
}