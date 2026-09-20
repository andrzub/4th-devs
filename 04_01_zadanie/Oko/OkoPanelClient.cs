using System.Net;

namespace _04_01_zadanie.Oko;

public sealed class PanelAccessDeniedException(string message) : Exception(message);

/// <summary>
/// Read-only access to the operator console. Every request passes <see cref="OkoPanelGuard"/>
/// first, and the one POST this client can perform is the sign-in form — there is no code path
/// here that submits anything else, so the run cannot leave a trace in the interface even if the
/// model asks it to.
/// </summary>
public sealed class OkoPanelClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _login;
    private readonly string _password;
    private readonly string _accessKey;
    private readonly string? _snapshotDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _signedIn;

    public OkoPanelClient(string baseUrl, string login, string password, string accessKey, string? snapshotDirectory = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _login = login;
        _password = password;
        _accessKey = accessKey;
        _snapshotDirectory = snapshotDirectory;

        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(1) };
    }

    public int RequestsSent { get; private set; }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_password) || string.IsNullOrWhiteSpace(_accessKey))
            throw new InvalidOperationException("Missing console credentials — set OkoEditor:PanelPassword and AI_DevsApiKey in appsettings.Development.json.");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "login",
            ["login"] = _login,
            ["password"] = _password,
            ["access_key"] = _accessKey
        });

        using var response = await _http.PostAsync($"{_baseUrl}/", form, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        RequestsSent++;

        if (!response.IsSuccessStatusCode)
            throw new PanelAccessDeniedException($"The console refused the sign-in with HTTP {(int)response.StatusCode}.");

        if (RecordParser.IsLoginPage(html))
            throw new PanelAccessDeniedException("The console answered the sign-in with the sign-in form again — check the login, password and access key.");

        _signedIn = true;
    }

    /// <summary>Fetches one readable page of the console, signing in first when the session is gone.</summary>
    public async Task<string> GetHtmlAsync(string path, CancellationToken cancellationToken = default)
    {
        var verdict = OkoPanelGuard.Evaluate(path);
        if (!verdict.Allowed)
            throw new PanelAccessDeniedException(verdict.Reason);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_signedIn)
                await SignInAsync(cancellationToken);

            var html = await FetchAsync(verdict.Path, cancellationToken);

            if (RecordParser.IsLoginPage(html))
            {
                await SignInAsync(cancellationToken);
                html = await FetchAsync(verdict.Path, cancellationToken);
            }

            return html;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OkoRecord>> ListAsync(string page, CancellationToken cancellationToken = default)
    {
        if (!OkoPages.Exists(page))
            throw new PanelAccessDeniedException($"'{page}' is not a page of the console. Pages: {string.Join(", ", OkoPages.All)}.");

        var html = await GetHtmlAsync("/" + page.ToLowerInvariant(), cancellationToken);
        return RecordParser.ParseListing(page.ToLowerInvariant(), html);
    }

    public async Task<OkoRecord> ReadAsync(string page, string id, CancellationToken cancellationToken = default)
    {
        var html = await GetHtmlAsync($"/{page.ToLowerInvariant()}/{id.ToLowerInvariant()}", cancellationToken);
        return RecordParser.ParseDetail(page.ToLowerInvariant(), id.ToLowerInvariant(), html);
    }

    private async Task<string> FetchAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{_baseUrl}{path}", cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        RequestsSent++;

        if (!response.IsSuccessStatusCode)
            throw new PanelAccessDeniedException($"The console answered HTTP {(int)response.StatusCode} for '{path}'.");

        SaveSnapshot(path, html);
        return html;
    }

    private void SaveSnapshot(string path, string html)
    {
        if (_snapshotDirectory is null)
            return;

        Directory.CreateDirectory(_snapshotDirectory);
        var name = path.Trim('/').Replace('/', '-');
        File.WriteAllText(Path.Combine(_snapshotDirectory, (name.Length == 0 ? "index" : name) + ".html"), html);
    }

    public void Dispose() => _http.Dispose();
}
