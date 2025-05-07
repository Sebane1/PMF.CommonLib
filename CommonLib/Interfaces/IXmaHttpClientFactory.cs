namespace CommonLib.Interfaces;

public interface IXmaHttpClientFactory
{
    /// <summary>
    /// Returns an HttpClient that includes a fresh or updated 'connect.sid' cookie if present.
    /// </summary>
    HttpClient CreateClient();
}