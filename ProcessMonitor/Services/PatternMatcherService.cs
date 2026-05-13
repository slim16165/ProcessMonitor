using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class PatternMatcherService
{
    private static readonly List<PatternDefinition> _patterns = new();
    private const string PatternsFileName = "pattern_definitions.json";

    static PatternMatcherService()
    {
        LoadPatterns();
        LoadDefaultPatterns();
    }

    public static PatternMatchResult MatchPattern(ProcessFingerprint fingerprint, ProcessMetrics currentMetrics, ProcessMetrics? baseline = null)
    {
        var matches = new List<PatternMatch>();

        foreach (var pattern in _patterns)
        {
            var match = EvaluatePattern(pattern, fingerprint, currentMetrics, baseline);
            if (match != null)
            {
                matches.Add(match);
            }
        }

        return new PatternMatchResult
        {
            Matches = matches.OrderByDescending(m => m.Confidence).ToList(),
            BestMatch = matches.OrderByDescending(m => m.Confidence).FirstOrDefault(),
            OverallConfidence = matches.Any() ? matches.Max(m => m.Confidence) : 0
        };
    }

    public static PatternMatchResult MatchSystemPattern(SystemHealthSnapshot snapshot)
    {
        var matches = new List<PatternMatch>();

        foreach (var pattern in _patterns)
        {
            if (pattern.Scope == PatternScope.System)
            {
                var match = EvaluateSystemPattern(pattern, snapshot);
                if (match != null)
                {
                    matches.Add(match);
                }
            }
        }

        return new PatternMatchResult
        {
            Matches = matches.OrderByDescending(m => m.Confidence).ToList(),
            BestMatch = matches.OrderByDescending(m => m.Confidence).FirstOrDefault(),
            OverallConfidence = matches.Any() ? matches.Max(m => m.Confidence) : 0
        };
    }

    private static PatternMatch? EvaluatePattern(PatternDefinition pattern, ProcessFingerprint fingerprint, ProcessMetrics currentMetrics, ProcessMetrics? baseline)
    {
        var indicators = new List<string>();
        var confidence = 0.0;

        foreach (var indicator in pattern.Indicators)
        {
            var match = EvaluateIndicator(indicator, fingerprint, currentMetrics, baseline);
            if (match.IsMatch)
            {
                indicators.Add(indicator.Name);
                confidence += indicator.Weight;
            }
        }

        // Normalize confidence to 0-1 range
        confidence = Math.Min(confidence, 1.0);

        if (confidence >= pattern.MinConfidence)
        {
            return new PatternMatch
            {
                PatternId = pattern.Id,
                PatternName = pattern.Name,
                Confidence = confidence,
                Indicators = indicators,
                RecommendedActions = pattern.RecommendedActions,
                Severity = confidence >= 0.8 ? "high" : confidence >= 0.5 ? "medium" : "low"
            };
        }

        return null;
    }

    private static PatternMatch? EvaluateSystemPattern(PatternDefinition pattern, SystemHealthSnapshot snapshot)
    {
        var indicators = new List<string>();
        var confidence = 0.0;

        foreach (var indicator in pattern.Indicators)
        {
            var match = EvaluateSystemIndicator(indicator, snapshot);
            if (match.IsMatch)
            {
                indicators.Add(indicator.Name);
                confidence += indicator.Weight;
            }
        }

        confidence = Math.Min(confidence, 1.0);

        if (confidence >= pattern.MinConfidence)
        {
            return new PatternMatch
            {
                PatternId = pattern.Id,
                PatternName = pattern.Name,
                Confidence = confidence,
                Indicators = indicators,
                RecommendedActions = pattern.RecommendedActions,
                Severity = confidence >= 0.8 ? "high" : confidence >= 0.5 ? "medium" : "low"
            };
        }

        return null;
    }

    private static (bool IsMatch, double Weight) EvaluateIndicator(PatternIndicator indicator, ProcessFingerprint fingerprint, ProcessMetrics currentMetrics, ProcessMetrics? baseline)
    {
        var weight = 0.0;

        switch (indicator.Type)
        {
            case IndicatorType.ProcessName:
                if (string.Equals(fingerprint.ProcessName, indicator.Value, StringComparison.OrdinalIgnoreCase))
                    weight = indicator.Weight;
                break;

            case IndicatorType.CommandLine:
                if (!string.IsNullOrEmpty(fingerprint.CommandLine) && 
                    fingerprint.CommandLine.Contains(indicator.Value, StringComparison.OrdinalIgnoreCase))
                    weight = indicator.Weight;
                break;

            case IndicatorType.CpuPercent:
                var cpuThreshold = double.Parse(indicator.Value);
                if (currentMetrics.CpuPercent >= cpuThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.MemoryMB:
                var memThreshold = double.Parse(indicator.Value);
                if (currentMetrics.MemoryMB >= memThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.HandleCount:
                var handleThreshold = double.Parse(indicator.Value);
                if (currentMetrics.HandleCount >= handleThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.IOMBps:
                var ioThreshold = double.Parse(indicator.Value);
                if ((currentMetrics.ReadMBps + currentMetrics.WriteMBps) >= ioThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.HandleCountVsBaseline:
                if (baseline != null && baseline.HandleCount > 0)
                {
                    var ratio = (double)currentMetrics.HandleCount / baseline.HandleCount;
                    var threshold = double.Parse(indicator.Value);
                    if (ratio >= threshold)
                        weight = indicator.Weight;
                }
                break;

            case IndicatorType.Duration:
                var durationThreshold = TimeSpan.Parse(indicator.Value);
                if (currentMetrics.Duration >= durationThreshold)
                    weight = indicator.Weight;
                break;
        }

        return (weight > 0, weight);
    }

    private static (bool IsMatch, double Weight) EvaluateSystemIndicator(PatternIndicator indicator, SystemHealthSnapshot snapshot)
    {
        var weight = 0.0;

        switch (indicator.Type)
        {
            case IndicatorType.DiskBusyPercent:
                var diskThreshold = double.Parse(indicator.Value);
                if (snapshot.DiskBusyPercent >= diskThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.PagesPerSec:
                var pagesThreshold = double.Parse(indicator.Value);
                if (snapshot.PagesPerSec >= pagesThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.AvailableMemoryMB:
                var memThreshold = double.Parse(indicator.Value);
                if (snapshot.AvailableMemoryMB <= memThreshold)
                    weight = indicator.Weight;
                break;

            case IndicatorType.DiskQueue:
                var queueThreshold = double.Parse(indicator.Value);
                if (snapshot.DiskQueueLength >= queueThreshold)
                    weight = indicator.Weight;
                break;
        }

        return (weight > 0, weight);
    }

    private static void LoadDefaultPatterns()
    {
        // Add default patterns if none exist
        if (_patterns.Count == 0)
        {
            _patterns.AddRange(GetDefaultPatterns());
            SavePatterns();
        }
    }

    private static List<PatternDefinition> GetDefaultPatterns()
    {
        return new List<PatternDefinition>
        {
            new PatternDefinition
            {
                Id = "disk_busy_over_100_percent",
                Name = "Disk I/O Saturation",
                Scope = PatternScope.System,
                MinConfidence = 0.6,
                Indicators = new List<PatternIndicator>
                {
                    new PatternIndicator { Type = IndicatorType.DiskBusyPercent, Value = "100", Weight = 0.8 },
                    new PatternIndicator { Type = IndicatorType.DiskQueue, Value = "1.0", Weight = 0.4 }
                },
                RecommendedActions = new List<string>
                {
                    "osservare 60-120s",
                    "verificare processi con alto I/O",
                    "controllare handle count dei processi sospetti"
                }
            },
            new PatternDefinition
            {
                Id = "paging_without_ram_exhaustion",
                Name = "Paging Anomalo",
                Scope = PatternScope.System,
                MinConfidence = 0.6,
                Indicators = new List<PatternIndicator>
                {
                    new PatternIndicator { Type = IndicatorType.PagesPerSec, Value = "1000", Weight = 0.8 },
                    new PatternIndicator { Type = IndicatorType.AvailableMemoryMB, Value = "2000", Weight = 0.5 }
                },
                RecommendedActions = new List<string>
                {
                    "verificare processi con working set elevato",
                    "controllare I/O di background",
                    "identificare processo con memory leak potenziale"
                }
            },
            new PatternDefinition
            {
                Id = "handle_count_anomaly",
                Name = "Handle Count Anomalo",
                Scope = PatternScope.Process,
                MinConfidence = 0.5,
                Indicators = new List<PatternIndicator>
                {
                    new PatternIndicator { Type = IndicatorType.HandleCountVsBaseline, Value = "3.0", Weight = 0.6 },
                    new PatternIndicator { Type = IndicatorType.HandleCount, Value = "1000", Weight = 0.4 }
                },
                RecommendedActions = new List<string>
                {
                    "osservare 60-120s",
                    "verificare parent/command line",
                    "terminare manualmente se persiste"
                }
            }
        };
    }

    private static void SavePatterns()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, PatternsFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_patterns, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PatternMatcherService] Error saving patterns: {ex.Message}");
        }
    }

    private static void LoadPatterns()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, PatternsFileName);
            
            if (!File.Exists(fullPath))
                return;

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<List<PatternDefinition>>(json);
            if (loaded != null)
            {
                _patterns.Clear();
                _patterns.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PatternMatcherService] Error loading patterns: {ex.Message}");
        }
    }
}

public class PatternDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PatternScope Scope { get; set; }
    public double MinConfidence { get; set; }
    public List<PatternIndicator> Indicators { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    public List<string> ForbiddenAutoActions { get; set; } = new();
}

public class PatternIndicator
{
    public IndicatorType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PatternMatchResult
{
    public List<PatternMatch> Matches { get; set; } = new();
    public PatternMatch? BestMatch { get; set; }
    public double OverallConfidence { get; set; }
}

public class PatternMatch
{
    public string PatternId { get; set; } = string.Empty;
    public string PatternName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Indicators { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    public string Severity { get; set; } = "low";
}

public enum PatternScope
{
    Process,
    System
}

public enum IndicatorType
{
    ProcessName,
    CommandLine,
    CpuPercent,
    MemoryMB,
    HandleCount,
    IOMBps,
    HandleCountVsBaseline,
    Duration,
    DiskBusyPercent,
    PagesPerSec,
    AvailableMemoryMB,
    DiskQueue
}
