using System.Text;

namespace _03_04_zadanie.Api;

/// <summary>
/// The single road to the central. Registering the tools and asking for the verdict are the same
/// endpoint with a different body, so both go through one method and both land in the log.
/// </summary>
public sealed class HubClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly RequestLog log;
    private readonly string verifyUrl;

    public HubClient(string verifyUrl, RequestLog log)
    {
        this.verifyUrl = verifyUrl;
        this.log = log;
    }

    public async Task<(int StatusCode, string Body)> PostAsync(string kind, string payload)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(verifyUrl, content);
        var body = await response.Content.ReadAsStringAsync();

        log.WriteVerify(kind, payload, (int)response.StatusCode, body);
        return ((int)response.StatusCode, body);
    }

    public void Dispose() => http.Dispose();
}
