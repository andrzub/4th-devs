namespace _03_02_zadanie.Guard;

/// <summary>
/// The guard's behaviour pinned down offline, with no network and no API key. Every case here is
/// a way the run could have been banned, and each one can be re-checked in a second after a change
/// to the matching rules — which is the whole reason the blacklist lives in code.
/// </summary>
public static class GuardTestSuite
{
    private sealed record Case(string Name, string Command, bool ExpectAllowed, Action<CommandGuard>? Arrange = null);

    private const string FirmwareDirectory = "/opt/firmware/cooler";

    private static void AtFirmware(CommandGuard guard) => guard.ObserveWorkingDirectory(FirmwareDirectory);

    private static void UnreadGitignore(CommandGuard guard) => guard.Gitignore.NoteDiscovered(FirmwareDirectory);

    private static void LoadedGitignore(CommandGuard guard) =>
        guard.ObserveGitignore($"{FirmwareDirectory}/.gitignore", "# controller secrets\nsecrets/\n*.key\n!public.key\n/local.log\n");

    private static readonly Case[] Cases =
    [
        new("relative path before pwd is refused",      "cat settings.ini",                     false),
        new("pwd itself is always fine",                "pwd",                                  true),
        new("the binary runs by naming its path",       "/opt/firmware/cooler/cooler.bin",      true),
        new("the binary may take arguments",            "/opt/firmware/cooler/cooler.bin --check", true),
        new("reading /etc is refused",                  "cat /etc/passwd",                      false),
        new("listing /etc is refused",                  "ls /etc",                              false),
        new("entering /root is refused",                "cd /root",                             false),
        new("reading under /proc is refused",           "cat /proc/cpuinfo",                    false),
        new("the forbidden root itself is refused",     "cat /proc",                            false),
        new("listing the filesystem root is fine",      "ls /",                                 true),
        new("a name search is fine",                    "find *.ini",                           true),
        new("a path given to find is refused",          "find /etc/*",                          false),
        new("a pattern naming a forbidden dir",         "find etc",                             false),
        new("a wildcard around it is caught too",       "find *proc*",                          false),
        new("a name that merely contains one is fine",  "find protocol.txt",                    true),
        new("an undocumented verb never leaves",        "grep -r password /opt",                false),
        new("a chained command never leaves",           "cat /opt/a.txt; ls /etc",              false),
        new("traversal is collapsed before judging",    "cat /opt/../etc/passwd",               false),
        new("home-relative paths are refused",          "cat ~/notes.txt",                      false),
        new("reboot needs the dedicated tool",          "reboot",                               false),
        new("editline needs a real line number",        "editline /opt/a.ini abc value",        false),
        new("editline lines start at 1",                "editline /opt/a.ini 0 value",          false),
        new("editline writes a value with spaces",      "editline /opt/a.ini 4 mode = safe; boot", true),

        new("relative path after pwd resolves",         "cat settings.ini",                     true,  AtFirmware),
        new("relative traversal is caught too",         "cat ../../../etc/shadow",              false, AtFirmware),
        new("a relative name search still works",       "find settings.ini",                    true,  AtFirmware),

        new("an unread .gitignore blocks the dir",      $"cat {FirmwareDirectory}/settings.ini", false, UnreadGitignore),
        new("the .gitignore itself stays readable",     $"cat {FirmwareDirectory}/.gitignore",   true,  UnreadGitignore),
        new("a sibling directory stays open",           "cat /opt/firmware/readme.txt",          true,  UnreadGitignore),

        new("a blacklisted directory is refused",       $"cat {FirmwareDirectory}/secrets/pass.txt", false, LoadedGitignore),
        new("a blacklisted extension is refused",       $"rm {FirmwareDirectory}/master.key",        false, LoadedGitignore),
        new("a bare name matches at any depth",         $"cat {FirmwareDirectory}/sub/deep.key",     false, LoadedGitignore),
        new("a negated rule wins",                      $"cat {FirmwareDirectory}/public.key",       true,  LoadedGitignore),
        new("an anchored rule stays anchored",          $"cat {FirmwareDirectory}/sub/local.log",    true,  LoadedGitignore),
        new("an anchored rule still bites at its root", $"cat {FirmwareDirectory}/local.log",        false, LoadedGitignore),
        new("an unlisted file is untouched",            $"editline {FirmwareDirectory}/settings.ini 3 pass=x", true, LoadedGitignore)
    ];

    public static bool Run(IReadOnlyList<string> forbiddenRoots)
    {
        var failures = 0;

        foreach (var testCase in Cases)
        {
            var guard = new CommandGuard(forbiddenRoots);
            testCase.Arrange?.Invoke(guard);

            var decision = guard.Inspect(testCase.Command);
            var passed = decision.IsAllowed == testCase.ExpectAllowed;
            if (!passed)
                failures++;

            var verdict = decision.IsAllowed ? "allowed" : "denied";
            Console.WriteLine($"{(passed ? "  ok  " : "  FAIL")}  {testCase.Name,-42}  {verdict,-7}  {testCase.Command}");

            if (!passed)
                Console.WriteLine($"          expected {(testCase.ExpectAllowed ? "allowed" : "denied")}: {decision.Reason}");
        }

        Console.WriteLine();
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} guard cases passed.");
        return failures == 0;
    }
}
