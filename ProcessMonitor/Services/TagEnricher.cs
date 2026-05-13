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

            if (lower.Contains("git"))
                tags.Add("git");
            if (lower.Contains("git fetch") || lower.Contains("git pull") || lower.Contains("git-remote-https"))
                tags.Add("git-network");
            if (lower.Contains("powershell") || lower.Contains("pwsh"))
                tags.Add("powershell");
            if (lower.Contains("cursor"))
                tags.Add("cursor");
            if (lower.Contains("windsurf"))
                tags.Add("windsurf");
            if (lower.Contains("code"))
                tags.Add("vscode-family");
            if (lower.Contains("conhost") || lower.Contains("openconsole") || lower.Contains("windowsterminal"))
                tags.Add("console-host");
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
