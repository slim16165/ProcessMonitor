namespace ProcessMonitor.Models;

public class ZombieConsoleSession
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public bool IsResponding { get; set; }
    public int ThreadCount { get; set; }
    public bool HasActiveTcpConnections { get; set; }
    public List<string> WaitReason { get; set; } = new();
    
    public string FormattedUptime
    {
        get
        {
            if (Uptime.TotalDays >= 1)
                return $"{(int)Uptime.TotalDays}d {Uptime.Hours}h";
            else if (Uptime.TotalHours >= 1)
                return $"{Uptime.Hours}h {Uptime.Minutes}m";
            else
                return $"{Uptime.Minutes}m {Uptime.Seconds}s";
        }
    }
}

