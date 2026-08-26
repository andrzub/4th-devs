using System.Text;
using System.Text.Json;

namespace PackagesMcpServer;

/// <summary>
/// Thin wrapper over the hub packages API. Every action is a POST with raw JSON
/// carrying the api key, so a single method covers both actions.
/// </summary>
public sealed class PackagesApiClient
{
    private const string PackagesEndpoint = "https://hub.ag3nts.org/api/packages";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _apiKey;

    public PackagesApiClient(string apiKey) => _apiKey = apiKey;

    public Task<string> CheckAsync(string packageId, CancellationToken cancellationToken) =>
        PostAsync(new Dictionary<string, string>
        {
            ["apikey"] = _apiKey,
            ["action"] = "check",
            ["packageid"] = packageId
        }, cancellationToken);

    public Task<string> RedirectAsync(string packageId, string destination, string code, CancellationToken cancellationToken) =>
        PostAsync(new Dictionary<string, string>
        {
            ["apikey"] = _apiKey,
            ["action"] = "redirect",
            ["packageid"] = packageId,
            ["destination"] = destination,
            ["code"] = code
        }, cancellationToken);

    private async Task<string> PostAsync(Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(PackagesEndpoint, content, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return response.IsSuccessStatusCode
            ? body
            : $$"""{"error":"Packages API returned HTTP {{(int)response.StatusCode}}","body":{{JsonSerializer.Serialize(body)}}}""";
    }
}
