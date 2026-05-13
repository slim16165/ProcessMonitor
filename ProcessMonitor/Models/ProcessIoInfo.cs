namespace ProcessMonitor.Models;

public class ProcessIoInfo
{
    public int ProcessId { get; set; }
    public long ReadBytesPerSecond { get; set; }
    public long WriteBytesPerSecond { get; set; }
    public List<string> OpenFiles { get; set; } = new();
    
    public double ReadMBps => ReadBytesPerSecond / (1024.0 * 1024.0);
    public double WriteMBps => WriteBytesPerSecond / (1024.0 * 1024.0);
    public double TotalMBps => (ReadBytesPerSecond + WriteBytesPerSecond) / (1024.0 * 1024.0);
}

public class TcpConnectionInfo
{
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string State { get; set; } = string.Empty;
}

public class GitProcessDetails
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public string? GitCommand { get; set; }
    public string? GitRepository { get; set; }
    public DateTime StartTime { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public bool IsResponding { get; set; }
    public ProcessIoInfo IoInfo { get; set; } = new();
    public bool IsBlockedOnIo { get; set; }
}

