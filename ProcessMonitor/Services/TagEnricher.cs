using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class TagEnricher
{
    public void ApplyTags(List<ProcessTreeNode> snapshot)
    {
        foreach (var node in snapshot)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lower = $"{node.ProcessName} {node.CommandLine}".ToLowerInvariant();

            if (ProcessMonitorClassifier.IsGitRelatedProcess(node.ProcessName, node.CommandLine))
                tags.Add("git");
            if (ProcessMonitorClassifier.IsGitNetworkProcess(node.ProcessName, node.CommandLine))
                tags.Add("git-network");
            if (ProcessMonitorClassifier.IsPowerShellProcess(node.ProcessName, node.CommandLine))
                tags.Add("powershell");
            if (lower.Contains("cursor"))
                tags.Add("cursor");
            if (lower.Contains("windsurf"))
                tags.Add("windsurf");
            if (lower.Contains("antigravity"))
                tags.Add("antigravity");
            if (lower.Contains("code"))
                tags.Add("vscode-family");
            if (lower.Contains("conhost") || lower.Contains("openconsole") || lower.Contains("windowsterminal"))
                tags.Add("console-host");
            if (ProcessMonitorClassifier.IsBrowser(lower))
                tags.Add("browser");
            if (ProcessMonitorClassifier.IsSecurityTool(lower))
                tags.Add("security");
            if (ProcessMonitorClassifier.IsTuningTool(lower))
                tags.Add("tuning");
            if (ProcessMonitorClassifier.IsIndexer(lower))
                tags.Add("indexer");
            if (lower.Contains("tgitcache"))
                tags.Add("git-cache");
            if (ProcessMonitorClassifier.IsLanguageServer(lower))
                tags.Add("language-server");
            if (node.LaunchCategory == "Service")
                tags.Add("service");
            if (ProcessMonitorClassifier.IsDiagnosticNoise(node.ProcessName, node.CommandLine))
                tags.Add("diagnostics-noise");
            if (!node.IsResponding)
                tags.Add("blocked");
            if (node.HasTcpActivity)
                tags.Add("tcp-active");
            if (node.IsLeaf)
                tags.Add("leaf");
            if (node.CommandAnalysis.Warnings.Any())
                tags.Add("command-risk");
            if (node.LogicalOwnerProcessId.HasValue && node.LogicalOwnerProcessId.Value != node.ProcessId)
                tags.Add("owner-inherited");

            node.Tags = tags.OrderBy(t => t).ToList();
        }
    }
}
