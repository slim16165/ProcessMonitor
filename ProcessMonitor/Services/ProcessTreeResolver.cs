using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessTreeResolver
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly OwnerResolver _ownerResolver;
    private readonly TagEnricher _tagEnricher;

    public ProcessTreeResolver(
        ProcessSnapshotService snapshotService,
        OwnerResolver ownerResolver,
        TagEnricher tagEnricher)
    {
        _snapshotService = snapshotService;
        _ownerResolver = ownerResolver;
        _tagEnricher = tagEnricher;
    }

    public ProcessInvestigation InvestigateByPid(int processId)
    {
        var snapshot = _snapshotService.CaptureSnapshot();
        _ownerResolver.AssignOwners(snapshot);
        _tagEnricher.ApplyTags(snapshot);
        var byPid = snapshot.ToDictionary(n => n.ProcessId);
        if (!byPid.TryGetValue(processId, out var focus))
        {
            return new ProcessInvestigation
            {
                RootProcessId = processId,
                RootProcessName = "NotFound",
                Evidence = new List<ProcessEvidence>
                {
                    new() { ProcessId = processId, Kind = "Missing", Detail = "Processo non trovato nello snapshot corrente" }
                }
            };
        }

        var root = ResolveRoot(focus, byPid);
        BuildChildren(snapshot, byPid);
        var treeRoot = CloneTree(root, byPid, new HashSet<int>());
        var descendants = Flatten(treeRoot).ToList();
        var orphans = snapshot
            .Where(n => n.ParentProcessId.HasValue && !byPid.ContainsKey(n.ParentProcessId.Value))
            .Where(n => ProcessMonitorClassifier.IsConsoleLike(n.ProcessName, n.CommandLine))
            .OrderByDescending(n => n.StartTime)
            .ToList();

        var evidence = BuildEvidence(focus, descendants, orphans);

        return new ProcessInvestigation
        {
            RootProcessId = treeRoot.ProcessId,
            RootProcessName = treeRoot.ProcessName,
            Root = treeRoot,
            Orphans = orphans,
            FocusMatches = descendants.Where(n => n.ProcessId == processId).ToList(),
            Evidence = evidence,
            Owners = _ownerResolver.BuildOwnerSummary(descendants)
        };
    }

    private static void BuildChildren(List<ProcessTreeNode> snapshot, Dictionary<int, ProcessTreeNode> byPid)
    {
        foreach (var node in snapshot)
        {
            node.Children = new List<ProcessTreeNode>();
        }

        foreach (var node in snapshot)
        {
            if (node.ParentProcessId.HasValue && byPid.TryGetValue(node.ParentProcessId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
        }
    }

    private static ProcessTreeNode ResolveRoot(ProcessTreeNode focus, Dictionary<int, ProcessTreeNode> byPid)
    {
        var current = focus;
        var visited = new HashSet<int> { current.ProcessId };

        if (ProcessMonitorClassifier.IsOwnerAnchor(current.ProcessName, current.CommandLine))
        {
            return current;
        }

        while (current.ParentProcessId.HasValue &&
               byPid.TryGetValue(current.ParentProcessId.Value, out var parent) &&
               !visited.Contains(parent.ProcessId))
        {
            if (ProcessMonitorClassifier.IsOwnerAnchor(current.ProcessName, current.CommandLine))
            {
                break;
            }

            if (current.ParentStartTime.HasValue && parent.StartTime.HasValue &&
                parent.StartTime.Value > current.StartTime.GetValueOrDefault())
            {
                break;
            }

            visited.Add(parent.ProcessId);
            current = parent;
        }

        return current;
    }

    private static ProcessTreeNode CloneTree(ProcessTreeNode node, Dictionary<int, ProcessTreeNode> byPid, HashSet<int> visited)
    {
        visited.Add(node.ProcessId);
        return new ProcessTreeNode
        {
            ProcessId = node.ProcessId,
            ParentProcessId = node.ParentProcessId,
            ProcessName = node.ProcessName,
            CommandLine = node.CommandLine,
            ExecutablePath = node.ExecutablePath,
            WorkingDirectory = node.WorkingDirectory,
            Owner = node.Owner,
            SessionId = node.SessionId,
            OwnerId = node.OwnerId,
            OwnerPath = node.OwnerPath.ToList(),
            Tags = node.Tags.ToList(),
            LogicalOwnerProcessName = node.LogicalOwnerProcessName,
            LogicalOwnerProcessId = node.LogicalOwnerProcessId,
            StartTime = node.StartTime,
            ParentStartTime = node.ParentStartTime,
            CpuUsage = node.CpuUsage,
            MemoryMB = node.MemoryMB,
            IsResponding = node.IsResponding,
            ThreadCount = node.ThreadCount,
            ReadBytesPerSecond = node.ReadBytesPerSecond,
            WriteBytesPerSecond = node.WriteBytesPerSecond,
            HasTcpActivity = node.HasTcpActivity,
            CommandAnalysis = node.CommandAnalysis,
            Status = node.Status,
            LaunchCategory = node.LaunchCategory,
            Children = node.Children
                .Where(c => !visited.Contains(c.ProcessId) && byPid.ContainsKey(c.ProcessId))
                .OrderBy(c => c.StartTime)
                .Select(c => CloneTree(c, byPid, visited))
                .ToList()
        };
    }

    private static IEnumerable<ProcessTreeNode> Flatten(ProcessTreeNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static List<ProcessEvidence> BuildEvidence(ProcessTreeNode focus, List<ProcessTreeNode> descendants, List<ProcessTreeNode> orphans)
    {
        var evidence = new List<ProcessEvidence>();

        foreach (var node in descendants)
        {
            if (!node.IsResponding)
                evidence.Add(new ProcessEvidence { ProcessId = node.ProcessId, Kind = "Blocked", Detail = $"{node.ProcessName} non risponde" });
            if (node.HasTcpActivity)
                evidence.Add(new ProcessEvidence { ProcessId = node.ProcessId, Kind = "TcpActivity", Detail = $"{node.ProcessName} ha connessioni TCP attive" });
            if (node.CommandAnalysis.Warnings.Any())
                evidence.Add(new ProcessEvidence { ProcessId = node.ProcessId, Kind = "CommandRisk", Detail = string.Join("; ", node.CommandAnalysis.Warnings) });
            if (node.LaunchCategory == "IDE")
                evidence.Add(new ProcessEvidence { ProcessId = node.ProcessId, Kind = "LaunchProvenance", Detail = $"{node.ProcessName} classificato come processo originatore IDE" });
        }

        foreach (var orphan in orphans.Take(10))
        {
            evidence.Add(new ProcessEvidence { ProcessId = orphan.ProcessId, Kind = "Orphan", Detail = $"{orphan.ProcessName} senza parent vivo" });
        }

        if (!descendants.Any(n => n.ProcessId == focus.ProcessId))
        {
            evidence.Add(new ProcessEvidence { ProcessId = focus.ProcessId, Kind = "Focus", Detail = "PID investigato non incluso nel tree finale" });
        }

        return evidence;
    }
}
