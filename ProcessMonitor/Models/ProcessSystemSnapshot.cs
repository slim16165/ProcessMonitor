namespace ProcessMonitor.Models;

public class ProcessSystemSnapshot
{
    public string SnapshotId { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public List<ProcessSnapshotEntry> Processes { get; set; } = new();
}

public class ProcessSnapshotEntry
{
    public int ProcessId { get; set; }
    public int? ParentProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string? OwnerId { get; set; }
    public List<string> OwnerPath { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public double CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public bool HasTcpActivity { get; set; }
    public DateTime? StartTime { get; set; }
}

public class ProcessSnapshotDiff
{
    public string BaselineId { get; set; } = string.Empty;
    public string CurrentId { get; set; } = string.Empty;
    public int BaselineCount { get; set; }
    public int CurrentCount { get; set; }
    public int DeltaCount => CurrentCount - BaselineCount;
    public List<OwnerDelta> OwnerDeltas { get; set; } = new();
    public List<ProcessSignatureDelta> NewSignatures { get; set; } = new();
    public List<ProcessSignatureDelta> RemovedSignatures { get; set; } = new();
}

public class OwnerDelta
{
    public string OwnerId { get; set; } = string.Empty;
    public List<string> OwnerPath { get; set; } = new();
    public int BaselineCount { get; set; }
    public int CurrentCount { get; set; }
    public int DeltaCount => CurrentCount - BaselineCount;
}

public class ProcessSignatureDelta
{
    public string Signature { get; set; } = string.Empty;
    public string SignatureDetail { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? OwnerId { get; set; }
    public int BaselineCount { get; set; }
    public int CurrentCount { get; set; }
    public int DeltaCount => CurrentCount - BaselineCount;
}
