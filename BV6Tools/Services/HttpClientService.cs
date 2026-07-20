using System.Net.Http;

namespace BV6Tools.Services;

public class HttpClientService(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient httpClient = httpClientFactory.CreateClient();

    public async Task<byte[]> DownloadDataAsync(string url, CancellationToken token = default)
    {
        return await httpClient.GetByteArrayAsync(url, token);
    }

    public async Task<byte[]> DownloadDataAsync(HttpRequestMessage request, TimeSpan connectTimeout, CancellationToken token = default)
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        connectCts.CancelAfter(connectTimeout);

        HttpResponseMessage response;
        try
        {
            // Only waits for headers to start arriving, not the full body
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Url didn't respond within {connectTimeout.TotalSeconds}s");
        }

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