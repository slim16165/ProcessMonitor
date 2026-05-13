namespace ProcessMonitor.Models;

public class LargeGitRepository
{
    public string RepositoryPath { get; set; } = string.Empty;
    public string GitDirPath { get; set; } = string.Empty;
    public long GitDirSizeMB { get; set; }
    public long TotalSizeMB { get; set; }
    public int TrackedFileCount { get; set; }
    public bool IsLarge { get; set; }
    
    public string RepositoryName => Path.GetFileName(RepositoryPath);
    
    public string FormattedGitSize => $"{GitDirSizeMB:N0} MB";
    public string FormattedTotalSize => $"{TotalSizeMB:N0} MB";
}

