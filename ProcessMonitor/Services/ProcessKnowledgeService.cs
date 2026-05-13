using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class ProcessKnowledgeService
{
    private static Dictionary<string, ProcessKnowledgeEntry> _knowledgeBase = new();
    private const string KnowledgeFileName = "process_knowledge.json";
    private static readonly object _lock = new();

    static ProcessKnowledgeService()
    {
        LoadKnowledgeBase();
    }

    public static void LoadKnowledgeBase()
    {
        lock (_lock)
        {
            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var fullPath = Path.Combine(currentDir, KnowledgeFileName);
                
                if (!File.Exists(fullPath))
                {
                    LoadDefaultKnowledge();
                    SaveKnowledgeBase();
                    return;
                }

                var json = File.ReadAllText(fullPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, ProcessKnowledgeEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (loaded != null)
                    _knowledgeBase = loaded;
                else
                    LoadDefaultKnowledge();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessKnowledgeService] Error loading knowledge base: {ex.Message}");
                LoadDefaultKnowledge();
            }
        }
    }

    public static void SaveKnowledgeBase()
    {
        lock (_lock)
        {
            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var fullPath = Path.Combine(currentDir, KnowledgeFileName);
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_knowledgeBase, options);
                File.WriteAllText(fullPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessKnowledgeService] Error saving knowledge base: {ex.Message}");
            }
        }
    }

    public static ProcessKnowledgeEntry? GetKnowledge(string processName, string? processPath = null)
    {
        lock (_lock)
        {
            // Try exact match first
            if (_knowledgeBase.TryGetValue(processName.ToLowerInvariant(), out var entry))
                return entry;

            // Try path-based match
            if (!string.IsNullOrEmpty(processPath))
            {
                foreach (var kvp in _knowledgeBase)
                {
                    if (kvp.Value.MatchPaths != null && kvp.Value.MatchPaths.Any(p => processPath.Contains(p, StringComparison.OrdinalIgnoreCase)))
                        return kvp.Value;
                }
            }

            return null;
        }
    }

    public static List<ProcessKnowledgeEntry> FindMatches(ProcessFingerprint fingerprint, SystemHealthSnapshot? snapshot = null)
    {
        lock (_lock)
        {
            var matches = new List<ProcessKnowledgeEntry>();

            foreach (var kvp in _knowledgeBase)
            {
                var entry = kvp.Value;
                var matchScore = CalculateMatchScore(fingerprint, snapshot, entry);
                
                if (matchScore > 0)
                {
                    var matchedEntry = new ProcessKnowledgeEntry
                    {
                        ProcessName = entry.ProcessName,
                        Description = entry.Description,
                        Category = entry.Category,
                        TypicalBehavior = entry.TypicalBehavior,
                        KnownIssues = entry.KnownIssues,
                        Indicators = entry.Indicators,
                        RecommendedActions = entry.RecommendedActions,
                        Severity = entry.Severity,
                        MatchScore = matchScore,
                        MatchReasons = GetMatchReasons(fingerprint, snapshot, entry)
                    };
                    matches.Add(matchedEntry);
                }
            }

            return matches.OrderByDescending(m => m.MatchScore).ToList();
        }
    }

    public static void AddKnowledgeEntry(ProcessKnowledgeEntry entry)
    {
        lock (_lock)
        {
            var key = entry.ProcessName.ToLowerInvariant();
            _knowledgeBase[key] = entry;
            SaveKnowledgeBase();
        }
    }

    public static void UpdateKnowledgeEntry(string processName, Action<ProcessKnowledgeEntry> updateAction)
    {
        lock (_lock)
        {
            var key = processName.ToLowerInvariant();
            if (_knowledgeBase.TryGetValue(key, out var entry))
            {
                updateAction(entry);
                SaveKnowledgeBase();
            }
        }
    }

    public static List<ProcessKnowledgeEntry> GetAllEntries()
    {
        lock (_lock)
        {
            return _knowledgeBase.Values.ToList();
        }
    }

    private static double CalculateMatchScore(ProcessFingerprint fingerprint, SystemHealthSnapshot? snapshot, ProcessKnowledgeEntry entry)
    {
        double score = 0;
        var reasons = new List<string>();

        // Process name match
        if (fingerprint.ProcessName.Equals(entry.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.5;
        }

        // Path match
        if (entry.MatchPaths != null && fingerprint.Path != null)
        {
            foreach (var path in entry.MatchPaths)
            {
                if (fingerprint.Path.Contains(path, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.3;
                    break;
                }
            }
        }

        // Command line match
        if (entry.MatchCommandPatterns != null && fingerprint.CommandLine != null)
        {
            foreach (var pattern in entry.MatchCommandPatterns)
            {
                if (fingerprint.CommandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.2;
                    break;
                }
            }
        }

        // Publisher match
        if (!string.IsNullOrEmpty(entry.ExpectedPublisher) && 
            fingerprint.Publisher != null && 
            fingerprint.Publisher.Contains(entry.ExpectedPublisher, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        return Math.Min(score, 1.0);
    }

    private static List<string> GetMatchReasons(ProcessFingerprint fingerprint, SystemHealthSnapshot? snapshot, ProcessKnowledgeEntry entry)
    {
        var reasons = new List<string>();

        if (fingerprint.ProcessName.Equals(entry.ProcessName, StringComparison.OrdinalIgnoreCase))
            reasons.Add($"Process name match: {fingerprint.ProcessName}");

        if (entry.MatchPaths != null && fingerprint.Path != null)
        {
            foreach (var path in entry.MatchPaths)
            {
                if (fingerprint.Path.Contains(path, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Path contains: {path}");
                    break;
                }
            }
        }

        if (entry.MatchCommandPatterns != null && fingerprint.CommandLine != null)
        {
            foreach (var pattern in entry.MatchCommandPatterns)
            {
                if (fingerprint.CommandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Command line contains: {pattern}");
                    break;
                }
            }
        }

        return reasons;
    }

    private static void LoadDefaultKnowledge()
    {
        _knowledgeBase = new Dictionary<string, ProcessKnowledgeEntry>
        {
            ["backgrounddownload"] = new ProcessKnowledgeEntry
            {
                ProcessName = "BackgroundDownload",
                Description = "Windows background download service for updates",
                Category = "System",
                TypicalBehavior = "Should have low CPU and I/O during normal operation",
                KnownIssues = new List<string>
                {
                    "Can consume high I/O when downloading large updates",
                    "May accumulate high handle count if stuck",
                    "Can cause disk queue saturation"
                },
                Indicators = new List<KnowledgeIndicator>
                {
                    new KnowledgeIndicator { Type = "handle_count", Threshold = "> 1000", Weight = 0.3, Description = "High handle count indicates stuck operation" },
                    new KnowledgeIndicator { Type = "io_write", Threshold = "> 10 MB/s", Weight = 0.4, Description = "Sustained high write I/O" },
                    new KnowledgeIndicator { Type = "cpu", Threshold = "> 5%", Weight = 0.2, Description = "Elevated CPU during download" }
                },
                RecommendedActions = new List<KnowledgeAction>
                {
                    new KnowledgeAction { Type = "observe", Description = "Monitor for 5 minutes, should complete automatically", Priority = "low" },
                    new KnowledgeAction { Type = "manual_kill", Description = "If persists > 30 minutes with high I/O, consider manual termination", Priority = "medium" },
                    new KnowledgeAction { Type = "investigate", Description = "Check Windows Update status", Priority = "low" }
                },
                Severity = "medium",
                AutoRemediable = false
            },
            ["git"] = new ProcessKnowledgeEntry
            {
                ProcessName = "git",
                Description = "Git version control system",
                Category = "Development",
                TypicalBehavior = "Spikes during clone/pull/push operations, otherwise idle",
                KnownIssues = new List<string>
                {
                    "Can consume high CPU during large operations",
                    "May spawn many child processes",
                    "Network operations can timeout"
                },
                Indicators = new List<KnowledgeIndicator>
                {
                    new KnowledgeIndicator { Type = "cpu", Threshold = "> 20%", Weight = 0.3, Description = "High CPU during large operations" },
                    new KnowledgeIndicator { Type = "child_process_count", Threshold = "> 10", Weight = 0.2, Description = "Many child processes" }
                },
                RecommendedActions = new List<KnowledgeAction>
                {
                    new KnowledgeAction { Type = "observe", Description = "Git operations are normally transient, wait for completion", Priority = "low" },
                    new KnowledgeAction { Type = "investigate", Description = "Check repository size and operation type", Priority = "low" }
                },
                Severity = "low",
                AutoRemediable = false,
                MatchPaths = new List<string> { "git.exe", "/usr/bin/git" }
            },
            ["language_server"] = new ProcessKnowledgeEntry
            {
                ProcessName = "language_server",
                Description = "Language server for IDE features (autocomplete, diagnostics)",
                Category = "Development",
                TypicalBehavior = "Should be responsive but not resource-intensive",
                KnownIssues = new List<string>
                {
                    "Can consume high memory for large projects",
                    "May become unresponsive if project is corrupted",
                    "Can spawn many analysis threads"
                },
                Indicators = new List<KnowledgeIndicator>
                {
                    new KnowledgeIndicator { Type = "memory", Threshold = "> 1 GB", Weight = 0.4, Description = "High memory usage" },
                    new KnowledgeIndicator { Type = "cpu", Threshold = "> 10%", Weight = 0.3, Description = "Sustained high CPU" }
                },
                RecommendedActions = new List<KnowledgeAction>
                {
                    new KnowledgeAction { Type = "observe", Description = "Check if working on large file or complex analysis", Priority = "low" },
                    new KnowledgeAction { Type = "restart_ide", Description = "If unresponsive, restart the IDE/language server", Priority = "medium" }
                },
                Severity = "low",
                AutoRemediable = false
            }
        };
    }
}

public class ProcessKnowledgeEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Unknown";
    public string TypicalBehavior { get; set; } = string.Empty;
    public List<string> KnownIssues { get; set; } = new();
    public List<KnowledgeIndicator> Indicators { get; set; } = new();
    public List<KnowledgeAction> RecommendedActions { get; set; } = new();
    public string Severity { get; set; } = "medium"; // low, medium, high
    public bool AutoRemediable { get; set; } = false;
    
    // Matching criteria
    public List<string>? MatchPaths { get; set; }
    public List<string>? MatchCommandPatterns { get; set; }
    public string? ExpectedPublisher { get; set; }
    
    // Runtime fields (not persisted)
    public double MatchScore { get; set; }
    public List<string> MatchReasons { get; set; } = new();
}

public class KnowledgeIndicator
{
    public string Type { get; set; } = string.Empty; // cpu, memory, io_read, io_write, handle_count, etc.
    public string Threshold { get; set; } = string.Empty; // "> 100", "< 50", etc.
    public double Weight { get; set; } = 1.0; // 0.0 to 1.0
    public string Description { get; set; } = string.Empty;
}

public class KnowledgeAction
{
    public string Type { get; set; } = string.Empty; // observe, manual_kill, restart_ide, investigate, etc.
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium"; // low, medium, high
}
