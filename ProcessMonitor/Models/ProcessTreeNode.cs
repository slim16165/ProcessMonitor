namespace ProcessMonitor.Models;

public class ProcessTreeNode
{
    public int ProcessId { get; set; }
    public int? ParentProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Owner { get; set; }
    public int? SessionId { get; set; }
    public string? OwnerId { get; set; }
    public List<string> OwnerPath { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? LogicalOwnerProcessName { get; set; }
    public int? LogicalOwnerProcessId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? ParentStartTime { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public bool IsResponding { get; set; }
    public int ThreadCount { get; set; }
    public long ReadBytesPerSecond { get; set; }
    public long WriteBytesPerSecond { get; set; }
    public bool HasTcpActivity { get; set; }
    public CommandAnalysis CommandAnalysis { get; set; } = new();
    public ProcessStatus Status { get; set; }
    public string LaunchCategory { get; set; } = "Unknown";
    public List<ProcessTreeNode> Children { get; set; } = new();

    public TimeSpan? Uptime => StartTime.HasValue ? DateTime.Now - StartTime.Value : null;
    public bool IsLeaf => Children.Count == 0;
    public bool IsConsoleLike => Services.ProcessMonitorClassifier.IsConsoleLike(ProcessName, CommandLine);
}
