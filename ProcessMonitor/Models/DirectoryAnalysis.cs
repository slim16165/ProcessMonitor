namespace ProcessMonitor.Models;

public class DirectoryAnalysis
{
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public int MaxDepth { get; set; }
    public List<string> LargeDirectories { get; set; } = new();
    public bool AnalysisCompleted { get; set; }
    public string? TimeoutReason { get; set; }
}

public class ProblematicDirectory
{
    public string Path { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long Size { get; set; }
    public int Depth { get; set; }
    public string Reason { get; set; } = string.Empty;
}

