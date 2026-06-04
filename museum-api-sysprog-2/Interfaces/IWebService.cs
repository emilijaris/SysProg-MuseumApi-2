namespace museum_api_sysprog_1.Interfaces
{
    public interface IWebService
    {
        List<Painting> GetPainting(string query);
    }
}