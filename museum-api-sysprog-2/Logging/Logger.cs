namespace museum_api_sysprog_1.Logging;

public static class Logger
{
    //kako ne bi vise niti pokusalo da kuca u konzolu istovremeno
    private static readonly object _logLock = new();

    public static void Log(string entity, string message)
    {
        lock (_logLock)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {entity}: {message}");
        }
    }
}