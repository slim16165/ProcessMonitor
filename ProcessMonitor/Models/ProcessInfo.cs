namespace ProcessMonitor.Models;

public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; }
    public bool IsResponding { get; set; }
    public DateTime StartTime { get; set; }
    public long IoReadBytes { get; set; }
    public long IoWriteBytes { get; set; }
    public int ThreadCount { get; set; }
    public ProcessStatus Status { get; set; }
    public double MemoryGrowthRate { get; set; } // Bytes per secondo
    public TimeSpan Uptime => DateTime.Now - StartTime;
}

public enum ProcessStatus
{
    Normal,
    HighCpu,
    Blocked,
    HighMemory,
    HighIo,
    Suspicious
}

