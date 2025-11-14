namespace ProcessMonitor.Models;

public class ProcessMonitorConfig
{
    public int CheckIntervalSeconds { get; set; } = 5;
    public double CpuThreshold { get; set; } = 10.0;
    public int MemoryThresholdMB { get; set; } = 500;
    public int IoThresholdMBps { get; set; } = 10;
    public int BlockedProcessTimeoutMinutes { get; set; } = 5;
    public List<string> SuspiciousProcesses { get; set; } = new() { "python", "pwsh", "powershell", "node" };
    public List<string> ExcludedDirectories { get; set; } = new() { "node_modules", "vendor", ".git", ".vs" };
    public int MaxDirectoryDepth { get; set; } = 10;
    public int DirectoryAnalysisTimeoutSeconds { get; set; } = 5;
}

public class NotificationConfig
{
    public bool Enabled { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public bool LogToFile { get; set; } = true;
    public string LogPath { get; set; } = "logs/process-monitor.log";
}

public class ExternalToolsConfig
{
    public string? WhatIsHangPath { get; set; }
    public string? UIHangPath { get; set; }
    public string? ProcessExplorerPath { get; set; }
    public string? ProcmonPath { get; set; }
    public bool AutoAnalyzeBlockedProcesses { get; set; } = false;
    public string PreferredTool { get; set; } = "WhatIsHang";
}

public class ExternalTool
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

