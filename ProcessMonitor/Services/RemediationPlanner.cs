using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class RemediationPlanner
{
    public RemediationPlan BuildPlan(ProcessInvestigation investigation)
    {
        if (investigation.Root == null)
        {
            return new RemediationPlan
            {
                SafeToKill = false,
                Summary = "Nessun piano disponibile",
                Reasons = new List<string> { "Root non disponibile" }
            };
        }

        var nodes = Flatten(investigation.Root).ToList();
        var holdOpen = nodes
            .Where(ShouldHold)
            .Select(n => n.ProcessId)
            .Distinct()
            .ToList();

        var killOrder = nodes
            .Where(n => ShouldKillCandidate(n) && !holdOpen.Contains(n.ProcessId))
            .OrderByDescending(GetDepth)
            .ThenByDescending(n => n.StartTime)
            .Select(n => n.ProcessId)
            .Distinct()
            .ToList();

        var reasons = new List<string>();
        var actions = new List<SuggestedAction>();
        if (killOrder.Any())
        {
            reasons.Add("Leaf-first su processi senza attività TCP e con segnali console/git/agent");
            actions.Add(new SuggestedAction
            {
                Type = "kill-leaf",
                Severity = "medium",
                Confidence = "medium",
                Summary = "Colpisci prima i leaf console/git senza rete attiva",
                Detail = "Il kill order è già ordinato per minimizzare effetti collaterali.",
                ProcessIds = killOrder.ToList()
            });
        }
        if (holdOpen.Any())
        {
            reasons.Add("Sono stati preservati processi con rete attiva o classificati come root IDE");
            actions.Add(new SuggestedAction
            {
                Type = "observe",
                Severity = "medium",
                Confidence = "high",
                Summary = "Non toccare i root IDE o i processi con TCP attivo",
                Detail = "Questi PID restano in hold-open finché non c'è evidenza più forte.",
                ProcessIds = holdOpen.ToList()
            });
        }
        if (!killOrder.Any())
        {
            reasons.Add("Nessun candidato abbastanza sicuro da killare automaticamente");
            actions.Add(new SuggestedAction
            {
                Type = "recheck",
                Severity = "low",
                Confidence = "medium",
                Summary = "Raccogli più evidenze prima di intervenire",
                Detail = "Usa inspect, why-slow o uno snapshot live per chiarire il rischio."
            });
        }

        return new RemediationPlan
        {
            SafeToKill = killOrder.Any(),
            Summary = killOrder.Any()
                ? $"Proposti {killOrder.Count} processi in kill order prudente"
                : "Solo ispezione, nessun kill automatico suggerito",
            Severity = killOrder.Any() ? "medium" : "low",
            Confidence = holdOpen.Any() ? "high" : "medium",
            KillOrder = killOrder,
            HoldOpen = holdOpen,
            Reasons = reasons,
            SuggestedActions = actions
        };
    }

    private static bool ShouldHold(ProcessTreeNode node)
    {
        return node.HasTcpActivity || node.LaunchCategory == "IDE" || node.ProcessName.Contains("windsurf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldKillCandidate(ProcessTreeNode node)
    {
        if (node.Children.Count > 0 && node.LaunchCategory == "IDE")
            return false;

        if (node.IsLeaf && !node.HasTcpActivity && ProcessMonitorClassifier.IsConsoleLike(node.ProcessName, node.CommandLine))
            return true;

        if (node.Status == ProcessStatus.Blocked && node.LaunchCategory != "IDE")
            return true;

        return false;
    }

    private static int GetDepth(ProcessTreeNode node)
    {
        var depth = 0;
        var current = node;
        while (current.ParentProcessId.HasValue)
        {
            depth++;
            current = new ProcessTreeNode { ParentProcessId = null };
        }
        return depth;
    }

    private static IEnumerable<ProcessTreeNode> Flatten(ProcessTreeNode root)
    {
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }

        yield return root;
    }
}
