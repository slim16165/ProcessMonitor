using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class GitRepositoryMapper
{
    private static readonly Dictionary<string, List<GitRepository>> _processToRepositories = new();
    private static readonly Dictionary<int, DateTime> _processStartTimes = new();
    private const string ConfigFileName = "git_repositories.json";

    static GitRepositoryMapper()
    {
        LoadConfiguration();
    }

    public static void AddMapping(int processId, string repositoryPath, string branch = "main")
    {
        if (!_processToRepositories.ContainsKey(processId.ToString()))
            _processToRepositories[processId.ToString()] = new List<GitRepository>();

        _processToRepositories[processId.ToString()].Add(new GitRepository
        {
            Path = repositoryPath,
            Branch = branch,
            LastSeen = DateTime.Now
        });

        if (!_processStartTimes.ContainsKey(processId))
            _processStartTimes[processId] = DateTime.Now;

        SaveConfiguration();
    }

    public static List<GitRepository> GetRepositoriesForProcess(int processId)
    {
        return _processToRepositories.TryGetValue(processId.ToString(), out var repos) 
            ? repos 
            : new List<GitRepository>();
    }

    public static Dictionary<string, List<GitRepository>> GetAllMappings()
    {
        return new Dictionary<string, List<GitRepository>>(_processToRepositories);
    }

    public static void ClearMappings()
    {
        _processToRepositories.Clear();
        _processStartTimes.Clear();
        SaveConfiguration();
    }

    public static List<GitProcessStatus> GetStuckGitProcesses(List<ProcessTreeNode> processes, double cpuThreshold = 5.0, TimeSpan? stuckDuration = null)
    {
        var stuckDurationToUse = stuckDuration ?? TimeSpan.FromMinutes(5);
        var stuckProcesses = new List<GitProcessStatus>();
        var now = DateTime.Now;
        var gitProcesses = processes.Where(p => p.ProcessName.Equals("git.exe", StringComparison.OrdinalIgnoreCase)).ToList();

        // Calculate heuristic score
        var totalGitCpu = gitProcesses.Sum(p => p.CpuUsage);
        var highCpuCount = gitProcesses.Count(p => p.CpuUsage >= cpuThreshold);
        var gitProcessCount = gitProcesses.Count;
        
        // Adjust threshold based on number of git processes
        // If many git processes, lower the individual threshold
        var adjustedCpuThreshold = gitProcessCount > 5 ? cpuThreshold * 0.7 : cpuThreshold;
        var adjustedStuckDuration = gitProcessCount > 5 ? stuckDurationToUse * 0.6 : stuckDurationToUse;

        foreach (var process in gitProcesses)
        {
            var repos = GetRepositoriesForProcess(process.ProcessId);
            var startTime = _processStartTimes.TryGetValue(process.ProcessId, out var start) ? start : now;
            var duration = now - startTime;

            if (process.CpuUsage >= adjustedCpuThreshold && duration >= adjustedStuckDuration)
            {
                stuckProcesses.Add(new GitProcessStatus
                {
                    ProcessId = process.ProcessId,
                    ProcessName = process.ProcessName,
                    CpuUsage = process.CpuUsage,
                    Duration = duration,
                    CommandLine = process.CommandLine,
                    Repositories = repos,
                    Status = "STUCK"
                });
            }
            else if (process.CpuUsage >= adjustedCpuThreshold)
            {
                stuckProcesses.Add(new GitProcessStatus
                {
                    ProcessId = process.ProcessId,
                    ProcessName = process.ProcessName,
                    CpuUsage = process.CpuUsage,
                    Duration = duration,
                    CommandLine = process.CommandLine,
                    Repositories = repos,
                    Status = "ACTIVE"
                });
            }
        }

        // Add heuristic score to the last status or create a summary
        if (gitProcessCount > 0)
        {
            var heuristicScore = CalculateGitHeuristicScore(gitProcessCount, totalGitCpu, highCpuCount);
            Console.WriteLine($"Git heuristic: {gitProcessCount} processes, {totalGitCpu:F1}% total CPU, {highCpuCount} high CPU - Score: {heuristicScore:F1}");
        }

        return stuckProcesses;
    }

    private static double CalculateGitHeuristicScore(int processCount, double totalCpu, int highCpuCount)
    {
        // Score components:
        // - Many git processes: higher score
        // - High total CPU: higher score
        // - Many high-CPU processes: higher score
        
        var processScore = Math.Min(processCount * 5, 50); // Max 50 points for process count
        var cpuScore = Math.Min(totalCpu * 2, 30); // Max 30 points for total CPU
        var highCpuScore = Math.Min(highCpuCount * 10, 20); // Max 20 points for high-CPU count
        
        return processScore + cpuScore + highCpuScore;
    }

    private static void SaveConfiguration()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, ConfigFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_processToRepositories, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitRepositoryMapper] Error saving configuration: {ex.Message}");
        }
    }

    private static void LoadConfiguration()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, ConfigFileName);
            
            if (!File.Exists(fullPath))
                return;

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<GitRepository>>>(json);
            if (loaded != null)
            {
                _processToRepositories.Clear();
                foreach (var kvp in loaded)
                {
                    _processToRepositories[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GitRepositoryMapper] Error loading configuration: {ex.Message}");
        }
    }
}

public class GitRepository
{
    public string Path { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public DateTime LastSeen { get; set; } = DateTime.Now;
}

public class GitProcessStatus
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public TimeSpan Duration { get; set; }
    public string CommandLine { get; set; } = string.Empty;
    public List<GitRepository> Repositories { get; set; } = new();
    public string Status { get; set; } = "UNKNOWN";
}
