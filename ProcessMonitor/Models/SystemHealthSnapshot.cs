namespace ProcessMonitor.Models;

public class SystemHealthSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public TimeSpan MachineUptime { get; set; }
    public double TotalCpuPercent { get; set; }
    public double UserCpuPercent { get; set; }
    public double KernelCpuPercent { get; set; }
    public double InterruptCpuPercent { get; set; }
    public double DpcCpuPercent { get; set; }
    public double DiskBusyPercent { get; set; }
    public double DiskQueueLength { get; set; }
    public double DiskBytesPerSec { get; set; }
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }
    public double AvailableMemoryMB { get; set; }
    public double CommittedMemoryMB { get; set; }
    public double CommitLimitMB { get; set; }
    public double PagesPerSec { get; set; }
    public double PageReadsPerSec { get; set; }
    public double PageFileUsageMB { get; set; }
    public double PageFilePeakUsageMB { get; set; }
    public double PageFileAllocatedMB { get; set; }
    public PressureAssessment Pressure { get; set; } = new();
    public List<TopProcessSample> CpuTop { get; set; } = new();
    public List<TopProcessSample> IoTop { get; set; } = new();
    public List<TopProcessSample> MemoryTop { get; set; } = new();
    public List<OwnerPressureSummary> Suspects { get; set; } = new();
}

public class PressureAssessment
{
    public string PrimaryBottleneck { get; set; } = "No obvious bottleneck";
    public string? SecondaryBottleneck { get; set; }
    public string Summary { get; set; } = string.Empty;
    public double CpuScore { get; set; }
    public double DiskScore { get; set; }
    public double MemoryScore { get; set; }
}

public class TopProcessSample
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? OwnerId { get; set; }
    public List<string> OwnerPath { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string LaunchCategory { get; set; } = "Unknown";
    public double CpuPercent { get; set; }
    public double MemoryMB { get; set; }
    public double ReadMBps { get; set; }
    public double WriteMBps { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class OwnerPressureSummary
{
    public string OwnerId { get; set; } = string.Empty;
    public List<string> OwnerPath { get; set; } = new();
    public int ProcessCount { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryMB { get; set; }
    public double ReadMBps { get; set; }
    public double WriteMBps { get; set; }
    public List<string> Tags { get; set; } = new();
    public string DominantReason { get; set; } = string.Empty;
}

public class SlowdownDiagnosis
{
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public SystemHealthSnapshot Health { get; set; } = new();
    public List<DiagnosisReason> Reasons { get; set; } = new();
    public List<OwnerPressureSummary> Owners { get; set; } = new();
    public List<TopProcessSample> FocusProcesses { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class DiagnosisReason
{
    public string Code { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Confidence { get; set; } = "medium";
    public List<int> ProcessIds { get; set; } = new();
    public List<string> OwnerIds { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
    public string SuggestedNextAction { get; set; } = string.Empty;
}

public class SuggestedAction
{
    public string Type { get; set; } = "observe";
    public string Severity { get; set; } = "medium";
    public string Confidence { get; set; } = "medium";
    public string Summary { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public List<int> ProcessIds { get; set; } = new();
    public string? ToolHint { get; set; }
}

public class SlowdownPlan
{
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Confidence { get; set; } = "medium";
    public List<string> Reasons { get; set; } = new();
    public List<SuggestedAction> Actions { get; set; } = new();
}

public class HealthSnapshotDelta
{
    public string BaselineBottleneck { get; set; } = "Unknown";
    public string CurrentBottleneck { get; set; } = "Unknown";
    public double CpuDelta { get; set; }
    public double DiskDelta { get; set; }
    public double MemoryAvailableDeltaMB { get; set; }
    public double PageReadsDelta { get; set; }
}

public class TagDelta
{
    public string Tag { get; set; } = string.Empty;
    public int BaselineCount { get; set; }
    public int CurrentCount { get; set; }
    public int DeltaCount => CurrentCount - BaselineCount;
}
