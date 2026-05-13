namespace ProcessMonitor.Models;

public class InvestigationConfig
{
    public int HistoryRetentionCount { get; set; } = 100;
    public TimeSpan DefaultLookback { get; set; } = TimeSpan.FromHours(24);
    public bool EnableEventLogReading { get; set; } = true;
    public bool EnableProcessHistory { get; set; } = true;
    public string? PowerShellPath { get; set; } = "powershell.exe";
    public string? HandleExePath { get; set; } = "handle.exe";
    
    // Investigation thresholds
    public int HighHandleCountThreshold { get; set; } = 1000;
    public double HighCpuThreshold { get; set; } = 50.0; // percentage
    public long HighMemoryThreshold { get; set; } = 1024 * 1024 * 1024; // 1GB
    
    public static InvestigationConfig Default => new();
}
