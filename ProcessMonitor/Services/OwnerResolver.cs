using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class OwnerResolver
{
    public void AssignOwners(List<ProcessTreeNode> snapshot)
    {
        var byPid = snapshot.ToDictionary(n => n.ProcessId);

        foreach (var node in snapshot)
        {
            var ownerNode = ResolveLogicalOwner(node, byPid);
            var ownerPath = ProcessMonitorClassifier.BuildOwnerPath(ownerNode.ProcessName, ownerNode.CommandLine);

            node.OwnerId = string.Join("/", ownerPath);
            node.OwnerPath = ownerPath;
            node.LogicalOwnerProcessId = ownerNode.ProcessId;
            node.LogicalOwnerProcessName = ownerNode.ProcessName;
        }
    }

    public List<OwnerSummary> BuildOwnerSummary(IEnumerable<ProcessTreeNode> nodes)
    {
        return nodes
            .GroupBy(n => n.OwnerId ?? "Unknown")
            .Select(group => new OwnerSummary
            {
                OwnerId = group.Key,
                OwnerPath = group.First().OwnerPath,
                ProcessCount = group.Count(),
                ProcessIds = group.Select(n => n.ProcessId).OrderBy(id => id).ToList(),
                Tags = group.SelectMany(n => n.Tags).Distinct().OrderBy(t => t).ToList()
            })
            .OrderByDescending(s => s.ProcessCount)
            .ToList();
    }

    private static ProcessTreeNode ResolveLogicalOwner(ProcessTreeNode node, Dictionary<int, ProcessTreeNode> byPid)
    {
        var current = node;
        var visited = new HashSet<int>();

        while (true)
        {
            if (!visited.Add(current.ProcessId))
                return current;

            if (ProcessMonitorClassifier.IsOwnerAnchor(current.ProcessName, current.CommandLine))
                return current;

            if (!current.ParentProcessId.HasValue || !byPid.TryGetValue(current.ParentProcessId.Value, out var parent))
                return current;

            current = parent;
        }
    }
}
