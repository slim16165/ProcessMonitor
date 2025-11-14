namespace ProcessMonitor.Models;

public class ProcessMetrics
{
    public int ProcessId { get; set; }
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; }
    public long IoReadBytes { get; set; }
    public long IoWriteBytes { get; set; }
    public double IoReadBytesPerSecond { get; set; }
    public double IoWriteBytesPerSecond { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class CpuMetrics
{
    public double TotalCpuUsage { get; set; }
    public Dictionary<int, double> ProcessCpuUsage { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class MemoryMetrics
{
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public Dictionary<int, long> ProcessMemoryUsage { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class IoMetrics
{
    public int ProcessId { get; set; }
    public long ReadBytes { get; set; }
    public long WriteBytes { get; set; }
    public double ReadBytesPerSecond { get; set; }
    public double WriteBytesPerSecond { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

