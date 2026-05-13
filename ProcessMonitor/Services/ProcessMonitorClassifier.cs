namespace ProcessMonitor.Services;

public static class ProcessMonitorClassifier
{
    private static readonly string[] ConsoleTargets =
    [
        "cmd", "powershell", "pwsh", "bash", "sh", "zsh",
        "git", "git.exe", "git-remote-https", "git-credential-manager",
        "python", "pythonw", "node", "nodejs", "dotnet",
        "conhost", "openconsole", "windowsterminal", "wt",
        "cursor", "windsurf", "code", "devenv"
    ];

    public static bool IsConsoleLike(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        return ConsoleTargets.Any(lower.Contains);
    }

    public static string CategorizeLaunch(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        if (lower.Contains("git-remote-https") || lower.Contains("git fetch") || lower.Contains("git pull"))
            return "GitNetwork";
        if (lower.Contains("git"))
            return "Git";
        if (lower.Contains("powershell") || lower.Contains("pwsh"))
            return "PowerShell";
        if (lower.Contains("python"))
            return "Python";
        if (lower.Contains("node"))
            return "Node";
        if (lower.Contains("cursor") || lower.Contains("windsurf") || lower.Contains("code"))
            return "IDE";
        if (lower.Contains("conhost") || lower.Contains("openconsole") || lower.Contains("windowsterminal") || lower.Contains("wt"))
            return "ConsoleHost";
        return "Unknown";
    }

    public static bool IsOwnerAnchor(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        return lower.Contains("devenv") ||
               lower.Contains("cursor") ||
               lower.Contains("windsurf") ||
               lower.Contains("code") ||
               lower.Contains("visual studio");
    }

    public static List<string> BuildOwnerPath(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();

        if (lower.Contains("devenv") || lower.Contains("visual studio"))
            return ["IDE", "VisualStudio"];
        if (lower.Contains("cursor"))
            return ["IDE", "Cursor"];
        if (lower.Contains("windsurf"))
            return ["IDE", "Windsurf"];
        if (lower.Contains("code"))
            return ["IDE", "VSCodeFamily"];
        if (lower.Contains("git"))
            return ["SCM", "Git"];
        if (lower.Contains("powershell") || lower.Contains("pwsh"))
            return ["Terminal", "PowerShell"];
        if (lower.Contains("windowsterminal") || lower.Contains("openconsole") || lower.Contains("conhost"))
            return ["Terminal", "ConsoleHost"];
        if (lower.Contains("node"))
            return ["Runtime", "Node"];
        if (lower.Contains("python"))
            return ["Runtime", "Python"];

        return ["Unknown"];
    }
}
