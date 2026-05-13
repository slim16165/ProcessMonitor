using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class IncidentLoggerService
{
    private static List<ProcessIncident> _incidents = new();
    private const int MaxIncidents = 100;
    private const string IncidentsFileName = "process_incidents.ndjson";

    static IncidentLoggerService()
    {
        LoadIncidents();
    }

    public static void LogIncident(ProcessIncident incident)
    {
        incident.Timestamp = DateTime.Now;
        incident.IncidentId = GenerateIncidentId();
        
        _incidents.Add(incident);
        if (_incidents.Count > MaxIncidents)
            _incidents.RemoveAt(0);
        
        SaveIncident(incident);
    }

    public static void UpdateIncident(string incidentId, IncidentResolution resolution)
    {
        var incident = _incidents.FirstOrDefault(i => i.IncidentId == incidentId);
        if (incident != null)
        {
            incident.Resolution = resolution;
            incident.ResolvedAt = DateTime.Now;
            incident.Status = "resolved";
            
            SaveIncidents();
        }
    }

    public static List<ProcessIncident> GetIncidentsForProcess(int processId, int limit = 10)
    {
        return _incidents
            .Where(i => i.ProcessId == processId)
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static List<ProcessIncident> GetRecentIncidents(int limit = 20)
    {
        return _incidents
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static List<ProcessIncident> GetIncidentsByPattern(string patternName, int limit = 10)
    {
        return _incidents
            .Where(i => i.DetectedPattern == patternName)
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static ProcessIncident? GetOpenIncident(int processId)
    {
        return _incidents
            .Where(i => i.ProcessId == processId && i.Status == "open")
            .OrderByDescending(i => i.Timestamp)
            .FirstOrDefault();
    }

    private static string GenerateIncidentId()
    {
        return $"INC-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";
    }

    private static void SaveIncident(ProcessIncident incident)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, IncidentsFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = false };
            var json = JsonSerializer.Serialize(incident, options) + Environment.NewLine;
            File.AppendAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IncidentLoggerService] Error saving incident: {ex.Message}");
        }
    }

    private static void SaveIncidents()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, IncidentsFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = false };
            var lines = _incidents.Select(i => JsonSerializer.Serialize(i, options));
            File.WriteAllLines(fullPath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IncidentLoggerService] Error saving incidents: {ex.Message}");
        }
    }

    private static void LoadIncidents()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, IncidentsFileName);
            
            if (!File.Exists(fullPath))
                return;

            var lines = File.ReadAllLines(fullPath);
            _incidents.Clear();
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                try
                {
                    var incident = JsonSerializer.Deserialize<ProcessIncident>(line);
                    if (incident != null)
                        _incidents.Add(incident);
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Keep only recent incidents
            if (_incidents.Count > MaxIncidents)
            {
                _incidents = _incidents
                    .OrderByDescending(i => i.Timestamp)
                    .Take(MaxIncidents)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IncidentLoggerService] Error loading incidents: {ex.Message}");
        }
    }
}

public class ProcessIncident
{
    public string IncidentId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? ProcessPath { get; set; }
    public string? CommandLine { get; set; }
    public string? ParentProcessName { get; set; }
    
    // Metrics at incident time
    public TopProcessSample? ProcessSample { get; set; }
    
    // Detection
    public string DetectedPattern { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
    public List<string> Symptoms { get; set; } = new();
    public List<string> Indicators { get; set; } = new();
    
    // Context
    public string SystemPressure { get; set; } = string.Empty;
    public string? OwnerPath { get; set; }
    
    // Resolution
    public string Status { get; set; } = "open"; // open, resolved, ignored
    public DateTime? ResolvedAt { get; set; }
    public IncidentResolution? Resolution { get; set; }
    
    // Analysis
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = new();
}


public class IncidentResolution
{
    public string ResolvedBy { get; set; } = string.Empty; // spontaneous_exit, manual_kill, restart, unknown
    public string ActionTaken { get; set; } = string.Empty;
    public TimeSpan? DurationBeforeResolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool Successful { get; set; }
}
