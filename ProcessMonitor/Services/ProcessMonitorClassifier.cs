namespace ProcessMonitor.Services;

public static class ProcessMonitorClassifier
{
    private static readonly string[] GitProcessNames = ["git", "git.exe", "git-remote-https", "git-remote-https.exe", "git-credential-manager", "git-credential-manager.exe"];
    private static readonly string[] PowerShellProcessNames = ["powershell", "powershell.exe", "pwsh", "pwsh.exe"];
    private static readonly string[] NodeProcessNames = ["node", "node.exe", "nodejs", "nodejs.exe"];
    private static readonly string[] PythonProcessNames = ["python", "python.exe", "pythonw", "pythonw.exe"];

    private static readonly string[] ConsoleTargets =
    [
        "cmd", "powershell", "pwsh", "bash", "sh", "zsh",
        "git", "git.exe", "git-remote-https", "git-credential-manager",
        "python", "pythonw", "node", "nodejs", "dotnet",
        "conhost", "openconsole", "windowsterminal", "wt",
        "cursor", "windsurf", "code", "devenv", "antigravity"
    ];

    public static bool IsConsoleLike(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        return ConsoleTargets.Any(lower.Contains);
    }

    public static string CategorizeLaunch(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        if (IsSecurityTool(lower))
            return "Security";
        if (IsTuningTool(lower))
            return "Tuning";
        if (IsBrowser(lower))
            return "Browser";
        if (IsIndexer(lower))
            return "Indexer";
        if (IsLanguageServer(lower))
            return "LanguageServer";
        if (lower.Contains("tgitcache"))
            return "GitCache";
        if (IsGitNetworkProcess(processName, commandLine))
            return "GitNetwork";
        if (IsGitRelatedProcess(processName, commandLine))
            return "Git";
        if (IsPowerShellProcess(processName, commandLine))
            return "PowerShell";
        if (IsPythonProcess(processName, commandLine))
            return "Python";
        if (IsNodeProcess(processName, commandLine))
            return "Node";
        if (lower.Contains("cursor") || lower.Contains("windsurf") || lower.Contains("code") || lower.Contains("antigravity"))
            return "IDE";
        if (lower.Contains("conhost") || lower.Contains("openconsole") || lower.Contains("windowsterminal") || lower.Contains("wt"))
            return "ConsoleHost";
        if (lower.Contains("service"))
            return "Service";
        return "Unknown";
    }

    public static bool IsOwnerAnchor(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        return lower.Contains("devenv") ||
               lower.Contains("cursor") ||
               lower.Contains("windsurf") ||
               lower.Contains("antigravity") ||
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
        if (lower.Contains("antigravity"))
            return ["IDE", "Antigravity"];
        if (lower.Contains("code"))
            return ["IDE", "VSCodeFamily"];
        if (IsBrowser(lower))
            return ["Browser", ResolveBrowserName(lower)];
        if (IsSecurityTool(lower))
            return ["Security", ResolveSecurityName(lower)];
        if (IsTuningTool(lower))
            return ["SystemTools", "Tuning"];
        if (IsIndexer(lower))
            return ["Indexing", "Everything"];
        if (lower.Contains("tgitcache"))
            return ["SCM", "GitCache"];
        if (IsLanguageServer(lower))
            return ["IDE", "LanguageServer"];
        if (IsGitRelatedProcess(processName, commandLine))
            return ["SCM", "Git"];
        if (IsPowerShellProcess(processName, commandLine))
            return ["Terminal", "PowerShell"];
        if (lower.Contains("windowsterminal") || lower.Contains("openconsole") || lower.Contains("conhost"))
            return ["Terminal", "ConsoleHost"];
        if (IsNodeProcess(processName, commandLine))
            return ["Runtime", "Node"];
        if (IsPythonProcess(processName, commandLine))
            return ["Runtime", "Python"];

        return ["Unknown"];
    }

    public static bool IsBrowser(string value)
    {
        return value.Contains("chrome") || value.Contains("brave") || value.Contains("msedge") || value.Contains("webview");
    }

    public static bool IsSecurityTool(string value)
    {
        return value.Contains("sentinel") || value.Contains("defender") || value.Contains("forti") || value.Contains("crowdstrike");
    }

    public static bool IsTuningTool(string value)
    {
        return value.Contains("processlasso") || value.Contains("processgovernor");
    }

    public static bool IsIndexer(string value)
    {
        return value.Contains("everything");
    }

    public static bool IsLanguageServer(string value)
    {
        return value.Contains("language_server") || value.Contains("lsp");
    }

    public static bool IsInheritableWrapper(string processName, string commandLine)
    {
        return IsGitRelatedProcess(processName, commandLine) ||
               IsPowerShellProcess(processName, commandLine) ||
               IsNodeProcess(processName, commandLine) ||
               IsPythonProcess(processName, commandLine) ||
               commandLine.Contains("conhost", StringComparison.OrdinalIgnoreCase) ||
               commandLine.Contains("openconsole", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("conhost", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("openconsole", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("windowsterminal", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasIntrinsicOwnerClassification(string processName, string commandLine)
    {
        var ownerPath = BuildOwnerPath(processName, commandLine);
        return ownerPath.Count != 1 || !string.Equals(ownerPath[0], "Unknown", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDiagnosticNoise(string processName, string commandLine)
    {
        var lower = $"{processName} {commandLine}".ToLowerInvariant();
        return lower.Contains("processmonitor.exe") ||
               lower.Contains("wmiprvse") ||
               lower.Contains("perfmon") ||
               lower.Contains("get-ciminstance");
    }

    public static bool IsGitRelatedProcess(string processName, string commandLine)
    {
        var normalized = NormalizeProcessName(processName);
        if (GitProcessNames.Contains(normalized))
            return true;

        var command = commandLine.ToLowerInvariant();
        return command.Contains("\"git.exe\" ") ||
               command.Contains("\\git.exe\" ") ||
               command.Contains(" git ") ||
               command.StartsWith("git ", StringComparison.OrdinalIgnoreCase) ||
               command.Contains(" git.exe ");
    }

    public static bool IsGitNetworkProcess(string processName, string commandLine)
    {
        var normalized = NormalizeProcessName(processName);
        if (normalized is "git-remote-https" or "git-remote-https.exe")
            return true;

        var command = commandLine.ToLowerInvariant();
        return command.Contains(" git fetch") || command.Contains(" git pull") || command.Contains("git-remote-https");
    }

    public static bool IsPowerShellProcess(string processName, string commandLine)
    {
        var normalized = NormalizeProcessName(processName);
        return PowerShellProcessNames.Contains(normalized) || commandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNodeProcess(string processName, string commandLine)
    {
        var normalized = NormalizeProcessName(processName);
        return NodeProcessNames.Contains(normalized) || commandLine.Contains("\\node", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPythonProcess(string processName, string commandLine)
    {
        var normalized = NormalizeProcessName(processName);
        return PythonProcessNames.Contains(normalized) || commandLine.Contains("\\python", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string processName) => processName.Trim().ToLowerInvariant();

    private static string ResolveBrowserName(string value)
    {
        if (value.Contains("brave"))
            return "Brave";
        if (value.Contains("msedge") || value.Contains("webview"))
            return "EdgeFamily";
        return "ChromeFamily";
    }

    private static string ResolveSecurityName(string value)
    {
        if (value.Contains("sentinel"))
            return "SentinelOne";
        if (value.Contains("forti"))
            return "Fortinet";
        if (value.Contains("defender"))
            return "Defender";
        return "SecurityAgent";
    }
}
