using System;
using System.Net.Http;

namespace mRemoteNG.App.Update;

public static class InternetConnection
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public static bool IsPossible()
    {
        try
        {
            return Client.GetAsync("https://www.microsoft.com").Result.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}