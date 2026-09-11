using _03_02_zadanie.Guard;
using _03_02_zadanie.Mission;

namespace _03_02_zadanie.Shell;

/// <summary>
/// Everything that happens around a single command: the guard decides whether it may leave at all,
/// the client sends it, and what comes back is used to keep the mirrored filesystem state honest.
/// Both the agent's tool and the manual modes go through here, so a command typed by hand is
/// checked exactly as strictly as one the model came up with.
/// </summary>
public sealed class ShellSession(ShellClient client, CommandGuard guard, MissionState mission, int maxOutputCharacters)
{
    private readonly Dictionary<string, string> _fileCache = [];

    public CommandGuard Guard => guard;
    public ShellClient Client => client;

    /// <summary>The machine's own <c>help</c> output, kept verbatim for the system prompt.</summary>
    public string HelpText { get; private set; } = string.Empty;

    public int GuardDenials => guard.DenialCount;

    /// <summary>
    /// Orients the session before the agent gets a turn: who we are, where we are, and which
    /// directories declare a blacklist. Doing this in code means the guard is already armed with
    /// every .gitignore before the model's first command, instead of learning about them by
    /// walking into one.
    /// </summary>
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var help = await client.SendAsync("help", cancellationToken);
        HelpText = help.Render(maxOutputCharacters);
        Console.WriteLine($"[recon] help: {help.Lines.Count} commands");

        var user = await client.SendAsync("whoami", cancellationToken);
        Console.WriteLine($"[recon] whoami: {user.Text}");

        await SynchroniseWorkingDirectoryAsync(cancellationToken);

        var found = await client.SendAsync("find .gitignore", cancellationToken);
        var gitignorePaths = found.Lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('/') && line.EndsWith(".gitignore"))
            .Where(path => !guard.ForbiddenRoots.Any(root => PathResolver.IsWithin(path, root)))
            .ToList();

        Console.WriteLine($"[recon] .gitignore files: {(gitignorePaths.Count == 0 ? "none" : string.Join(", ", gitignorePaths))}");

        foreach (var path in gitignorePaths)
        {
            var content = await client.SendAsync($"cat {path}", cancellationToken);
            guard.ObserveGitignore(path, content.Text);
            Console.WriteLine($"[recon] blacklist from {path}: {content.Lines.Count} line(s)");
        }
    }

    public async Task<string> ExecuteAsync(string rawCommand, CancellationToken cancellationToken = default)
    {
        var decision = guard.Inspect(rawCommand);
        if (!decision.IsAllowed)
        {
            Console.WriteLine($"[guard] denied: {rawCommand}");
            return $"{decision.Reason}{Environment.NewLine}{Environment.NewLine}{Status()}";
        }

        var command = decision.Command!;

        if (TryServeFromCache(command, out var cached))
            return $"{cached}{Environment.NewLine}{Environment.NewLine}(unchanged since it was read earlier in this run, so no request was spent){Environment.NewLine}{Status()}";

        var response = await client.SendAsync(command.Raw, cancellationToken);
        Observe(command, response);
        mission.ScanForCode(response.RawBody);

        return $"{response.Render(maxOutputCharacters)}{Environment.NewLine}{Environment.NewLine}{Status()}";
    }

    public async Task<ShellResponse> RebootAsync(CancellationToken cancellationToken = default)
    {
        var response = await client.SendAsync("reboot", cancellationToken);
        _fileCache.Clear();
        guard.Paths.Desynchronise();
        await SynchroniseWorkingDirectoryAsync(cancellationToken);
        return response;
    }

    public string Status() =>
        $"[session] {client.RemainingRequests} of {client.RequestCount + client.RemainingRequests} shell commands left; {guard.Describe().ReplaceLineEndings("; ")}";

    private async Task SynchroniseWorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        var pwd = await client.SendAsync("pwd", cancellationToken);
        var directory = pwd.Lines.FirstOrDefault(line => line.Trim().StartsWith('/'))?.Trim();

        if (directory is not null)
            guard.ObserveWorkingDirectory(directory);
        else
            guard.Paths.Desynchronise();

        Console.WriteLine($"[recon] pwd: {directory ?? "unknown"}");
    }

    private bool TryServeFromCache(ShellCommand command, out string cached)
    {
        cached = string.Empty;
        if (command.Verb != "cat")
            return false;

        var path = ResolvedPathOf(command);
        return path is not null && _fileCache.TryGetValue(path, out cached!);
    }

    /// <summary>
    /// Keeps the mirrored state in step with the machine: where we are, what the blacklists say,
    /// and which file contents are still worth reusing.
    /// </summary>
    private void Observe(ShellCommand command, ShellResponse response)
    {
        var path = ResolvedPathOf(command);

        switch (command.Verb)
        {
            case "pwd":
                var reported = response.Lines.FirstOrDefault(line => line.Trim().StartsWith('/'))?.Trim();
                if (reported is not null)
                    guard.ObserveWorkingDirectory(reported);
                break;

            case "cd":
                guard.ObserveDirectoryChange(command, response.IsHttpSuccess);
                break;

            case "cat" when path is not null && PathResolver.NameOf(path) == ".gitignore" && response.IsHttpSuccess:
                guard.ObserveGitignore(path, response.Text);
                break;

            case "cat" when path is not null && response.IsHttpSuccess:
                _fileCache[path] = response.Render(maxOutputCharacters);
                guard.ObserveListing(path, EntryNames(response));
                break;

            case "ls" when response.IsHttpSuccess:
                guard.ObserveListing(path ?? guard.Paths.CurrentDirectory, EntryNames(response));
                break;

            case "find" when response.IsHttpSuccess:
                NoteDiscoveredBlacklists(response);
                break;

            case "editline" or "rm":
                // A write anywhere can change what a later read would return, so nothing stays cached.
                _fileCache.Clear();
                break;
        }
    }

    private void NoteDiscoveredBlacklists(ShellResponse response)
    {
        var paths = response.Lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('/') && PathResolver.NameOf(line) == ".gitignore")
            .Where(path => !guard.ForbiddenRoots.Any(root => PathResolver.IsWithin(path, root)));

        foreach (var path in paths)
            guard.Gitignore.NoteDiscovered(PathResolver.DirectoryOf(path));
    }

    private static IEnumerable<string> EntryNames(ShellResponse response) => response.Lines
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Select(line => PathResolver.NameOf(line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[^1]));

    private string? ResolvedPathOf(ShellCommand command)
    {
        var argument = command.PathArguments.FirstOrDefault();
        if (argument is null)
            return null;

        return guard.Paths.TryResolve(argument.Value, out var absolute, out _) ? absolute : null;
    }
}
