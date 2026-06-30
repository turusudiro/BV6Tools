using System.Net.Http;

namespace BV6Tools.Services;

public class HttpClientService
{
    private readonly HttpClient httpClient;

    public HttpClientService(IHttpClientFactory httpClientFactory)
    {
        httpClient = httpClientFactory.CreateClient();
    }

    public async Task<byte[]> DownloadDataAsync(string url)
    {
        return await httpClient.GetByteArrayAsync(url);
    }

    public async Task<byte[]> DownloadDataAsync(string url, HttpRequestMessage httpRequestMessage)
    {
        var response = await httpClient.SendAsync(httpRequestMessage);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<string> DownloadStringAsync(string url)
    {
        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return string.Empty;
        return await response.Content.ReadAsStringAsync();
    }
}