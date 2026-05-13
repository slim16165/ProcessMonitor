using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessHistoryService
{
    private readonly ILogger<ProcessHistoryService>? _logger;
    private readonly InvestigationConfig _config;
    private const string HistoryFileName = "process_history.json";

    public ProcessHistoryService(InvestigationConfig config, ILogger<ProcessHistoryService>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public void AddSnapshot(ProcessInfo snapshot)
    {
        try
        {
            var history = LoadHistory();
            
            var entry = new ProcessHistoryEntry
            {
                Timestamp = DateTime.Now,
                ProcessId = snapshot.ProcessId,
                ProcessName = snapshot.ProcessName,
                CpuPercent = snapshot.CpuPercent,
                MemoryMB = snapshot.MemoryMB,
                HandleCount = snapshot.HandleCount,
                ThreadCount = snapshot.ThreadCount
            };

            history.Add(entry);
            
            // Retain only last N entries
            if (history.Count > _config.HistoryRetentionCount)
                history.RemoveRange(0, history.Count - _config.HistoryRetentionCount);

            SaveHistory(history);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'aggiunta snapshot alla cronologia");
        }
    }

    public List<ProcessHistoryEntry> GetHistory(int processId, int limit)
    {
        try
        {
            var history = LoadHistory();
            var processHistory = history
                .Where(h => h.ProcessId == processId)
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToList();

            return processHistory;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero cronologia processo {ProcessId}", processId);
            return new List<ProcessHistoryEntry>();
        }
    }

    public ProcessTrend? GetTrend(int processId, TimeSpan period)
    {
        try
        {
            var history = LoadHistory();
            var cutoff = DateTime.Now - period;
            
            var relevantEntries = history
                .Where(h => h.ProcessId == processId && h.Timestamp >= cutoff)
                .OrderBy(h => h.Timestamp)
                .ToList();

            if (relevantEntries.Count < 2)
                return null;

            var first = relevantEntries.First();
            var last = relevantEntries.Last();

            return new ProcessTrend
            {
                ProcessId = processId,
                Period = period,
                EntryCount = relevantEntries.Count,
                CpuTrend = last.CpuPercent - first.CpuPercent,
                MemoryTrend = last.MemoryMB - first.MemoryMB,
                HandleTrend = last.HandleCount - first.HandleCount,
                StartTimestamp = first.Timestamp,
                EndTimestamp = last.Timestamp
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel calcolo trend processo {ProcessId}", processId);
            return null;
        }
    }

    public List<ProcessHistoryEntry> GetRecentHistory(int limit = 50)
    {
        try
        {
            var history = LoadHistory();
            return history
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero cronologia recente");
            return new List<ProcessHistoryEntry>();
        }
    }

    private List<ProcessHistoryEntry> LoadHistory()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, HistoryFileName);
            
            if (!File.Exists(fullPath))
                return new List<ProcessHistoryEntry>();

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<List<ProcessHistoryEntry>>(json);
            
            return loaded ?? new List<ProcessHistoryEntry>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel caricamento cronologia");
            return new List<ProcessHistoryEntry>();
        }
    }

    private void SaveHistory(List<ProcessHistoryEntry> history)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, HistoryFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(history, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel salvataggio cronologia");
        }
    }
}

public class ProcessHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryMB { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
}

public class ProcessTrend
{
    public int ProcessId { get; set; }
    public TimeSpan Period { get; set; }
    public int EntryCount { get; set; }
    public double CpuTrend { get; set; }
    public long MemoryTrend { get; set; }
    public int HandleTrend { get; set; }
    public DateTime StartTimestamp { get; set; }
    public DateTime EndTimestamp { get; set; }
}
