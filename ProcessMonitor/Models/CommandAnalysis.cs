namespace ProcessMonitor.Models;

public class CommandAnalysis
{
    public string CommandLine { get; set; } = string.Empty;
    public bool HasRecursiveSearch { get; set; }
    public bool HasUnlimitedPath { get; set; }
    public bool HasNoDepthLimit { get; set; }
    public bool HasNoExclusions { get; set; }
    public List<string> Warnings { get; set; } = new();
    public RiskLevel RiskLevel { get; set; }
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

