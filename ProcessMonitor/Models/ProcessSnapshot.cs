namespace ProcessMonitor.Models;

public class ProcessSnapshot
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double MemoryMB { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
