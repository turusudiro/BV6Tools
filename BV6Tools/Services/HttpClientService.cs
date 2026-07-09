using System.Net.Http;

namespace BV6Tools.Services;

public class HttpClientService(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient httpClient = httpClientFactory.CreateClient();

    public async Task<byte[]> DownloadDataAsync(string url, CancellationToken token = default)
    {
        return await httpClient.GetByteArrayAsync(url, token);
    }

    public async Task<byte[]> DownloadDataAsync(HttpRequestMessage httpRequestMessage, CancellationToken token = default)
    {
        var response = await httpClient.SendAsync(httpRequestMessage, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(token);
    }

    public async Task<string> DownloadStringAsync(string url, CancellationToken token)
    {
        var response = await httpClient.GetAsync(url, token);
        if (!response.IsSuccessStatusCode) return string.Empty;
        return await response.Content.ReadAsStringAsync(token);
    }
}