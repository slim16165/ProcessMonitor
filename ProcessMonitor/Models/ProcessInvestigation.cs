namespace ProcessMonitor.Models;

public class ProcessInvestigation
{
    public int RootProcessId { get; set; }
    public string RootProcessName { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public ProcessTreeNode? Root { get; set; }
    public List<ProcessTreeNode> Orphans { get; set; } = new();
    public List<ProcessTreeNode> FocusMatches { get; set; } = new();
    public List<ProcessEvidence> Evidence { get; set; } = new();
    public List<OwnerSummary> Owners { get; set; } = new();
    public RemediationPlan RemediationPlan { get; set; } = new();
}

public class ProcessEvidence
{
    public int ProcessId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class RemediationPlan
{
    public bool SafeToKill { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<int> KillOrder { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
    public List<int> HoldOpen { get; set; } = new();
}

public class OwnerSummary
{
    public string OwnerId { get; set; } = string.Empty;
    public List<string> OwnerPath { get; set; } = new();
    public int ProcessCount { get; set; }
    public List<int> ProcessIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
