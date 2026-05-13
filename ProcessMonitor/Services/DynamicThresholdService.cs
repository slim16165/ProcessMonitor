using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class DynamicThresholdService
{
    private static readonly List<SystemHealthSnapshot> _history = new();
    private static readonly Dictionary<string, ProcessThresholdHistory> _processHistories = new();
    private const int MaxHistorySize = 10;
    private const int MaxProcessHistorySize = 20;
    private const string HistoryFileName = "threshold_history.json";
    private const string ProcessHistoriesFileName = "process_threshold_histories.json";

    static DynamicThresholdService()
    {
        LoadHistory();
        LoadProcessHistories();
    }

    public static void AddSnapshot(SystemHealthSnapshot snapshot)
    {
        _history.Add(snapshot);
        if (_history.Count > MaxHistorySize)
            _history.RemoveAt(0);
        
        SaveHistory();
    }

    public static void AddProcessMetrics(int processId, ProcessMetrics metrics, string? fingerprintHash = null)
    {
        var key = fingerprintHash ?? $"pid_{processId}";
        
        if (!_processHistories.ContainsKey(key))
            _processHistories[key] = new ProcessThresholdHistory { ProcessId = processId, FingerprintHash = fingerprintHash };

        var history = _processHistories[key];
        history.Metrics.Add(metrics);
        
        if (history.Metrics.Count > MaxProcessHistorySize)
            history.Metrics.RemoveAt(0);
        
        history.LastSeen = DateTime.Now;
        SaveProcessHistories();
    }

    public static ThresholdBaseline CalculateBaseline()
    {
        if (_history.Count < 2)
            return new ThresholdBaseline();

        var cpuValues = _history.Select(h => h.TotalCpuPercent).ToList();
        var diskValues = _history.Select(h => h.DiskBusyPercent).ToList();
        var memoryValues = _history.Select(h => h.AvailableMemoryMB).ToList();

        return new ThresholdBaseline
        {
            CpuMean = cpuValues.Average(),
            CpuStdDev = CalculateStdDev(cpuValues),
            CpuMin = cpuValues.Min(),
            CpuMax = cpuValues.Max(),
            DiskMean = diskValues.Average(),
            DiskStdDev = CalculateStdDev(diskValues),
            DiskMin = diskValues.Min(),
            DiskMax = diskValues.Max(),
            MemoryMean = memoryValues.Average(),
            MemoryStdDev = CalculateStdDev(memoryValues),
            MemoryMin = memoryValues.Min(),
            MemoryMax = memoryValues.Max(),
            SampleCount = _history.Count
        };
    }

    public static ProcessThresholdBaseline? CalculateProcessBaseline(int processId, string? fingerprintHash = null)
    {
        var key = fingerprintHash ?? $"pid_{processId}";
        
        if (!_processHistories.TryGetValue(key, out var history) || history.Metrics.Count < 3)
            return null;

        var cpuValues = history.Metrics.Select(m => m.CpuPercent).ToList();
        var memValues = history.Metrics.Select(m => m.MemoryMB).ToList();
        var readValues = history.Metrics.Select(m => m.ReadMBps).ToList();
        var writeValues = history.Metrics.Select(m => m.WriteMBps).ToList();
        var handleValues = history.Metrics.Select(m => (double)m.HandleCount).ToList();

        return new ProcessThresholdBaseline
        {
            ProcessId = processId,
            FingerprintHash = fingerprintHash,
            CpuMean = cpuValues.Average(),
            CpuStdDev = CalculateStdDev(cpuValues),
            CpuMin = cpuValues.Min(),
            CpuMax = cpuValues.Max(),
            MemoryMean = memValues.Average(),
            MemoryStdDev = CalculateStdDev(memValues),
            MemoryMin = memValues.Min(),
            MemoryMax = memValues.Max(),
            ReadMean = readValues.Average(),
            ReadStdDev = CalculateStdDev(readValues),
            WriteMean = writeValues.Average(),
            WriteStdDev = CalculateStdDev(writeValues),
            HandleCountMean = handleValues.Average(),
            HandleCountStdDev = CalculateStdDev(handleValues),
            HandleCountMin = handleValues.Min(),
            HandleCountMax = handleValues.Max(),
            SampleCount = history.Metrics.Count,
            LastSeen = history.LastSeen
        };
    }

    public static List<ProcessAnomalyAlert> DetectProcessAnomalies(int processId, ProcessMetrics current, string? fingerprintHash = null, double multiplier = 3.0)
    {
        var alerts = new List<ProcessAnomalyAlert>();
        
        var baseline = CalculateProcessBaseline(processId, fingerprintHash);
        if (baseline == null)
            return alerts;

        // CPU anomaly
        if (current.CpuPercent > baseline.CpuMean + (multiplier * baseline.CpuStdDev) && baseline.CpuMean > 1.0)
        {
            alerts.Add(new ProcessAnomalyAlert
            {
                Type = "CPU",
                Severity = current.CpuPercent > baseline.CpuMean + (5.0 * baseline.CpuStdDev) ? "high" : "medium",
                CurrentValue = current.CpuPercent,
                BaselineMean = baseline.CpuMean,
                BaselineStdDev = baseline.CpuStdDev,
                DeviationMultiplier = current.CpuPercent / baseline.CpuMean
            });
        }

        // Memory anomaly
        if (current.MemoryMB > baseline.MemoryMean + (multiplier * baseline.MemoryStdDev) && baseline.MemoryMean > 10.0)
        {
            alerts.Add(new ProcessAnomalyAlert
            {
                Type = "Memory",
                Severity = current.MemoryMB > baseline.MemoryMean + (5.0 * baseline.MemoryStdDev) ? "high" : "medium",
                CurrentValue = current.MemoryMB,
                BaselineMean = baseline.MemoryMean,
                BaselineStdDev = baseline.MemoryStdDev,
                DeviationMultiplier = current.MemoryMB / baseline.MemoryMean
            });
        }

        // Handle count anomaly (relative to baseline)
        if (current.HandleCount > baseline.HandleCountMean + (multiplier * baseline.HandleCountStdDev) && baseline.HandleCountMean > 10.0)
        {
            alerts.Add(new ProcessAnomalyAlert
            {
                Type = "HandleCount",
                Severity = current.HandleCount > baseline.HandleCountMean + (5.0 * baseline.HandleCountStdDev) ? "high" : "medium",
                CurrentValue = current.HandleCount,
                BaselineMean = baseline.HandleCountMean,
                BaselineStdDev = baseline.HandleCountStdDev,
                DeviationMultiplier = current.HandleCount / baseline.HandleCountMean
            });
        }

        // I/O anomaly
        var totalIo = current.ReadMBps + current.WriteMBps;
        var baselineIo = baseline.ReadMean + baseline.WriteMean;
        if (totalIo > baselineIo + (multiplier * (baseline.ReadStdDev + baseline.WriteStdDev)) && baselineIo > 1.0)
        {
            alerts.Add(new ProcessAnomalyAlert
            {
                Type = "I/O",
                Severity = totalIo > baselineIo + (5.0 * (baseline.ReadStdDev + baseline.WriteStdDev)) ? "high" : "medium",
                CurrentValue = totalIo,
                BaselineMean = baselineIo,
                BaselineStdDev = baseline.ReadStdDev + baseline.WriteStdDev,
                DeviationMultiplier = totalIo / baselineIo
            });
        }

        return alerts;
    }

    public static List<AnomalyAlert> DetectAnomalies(SystemHealthSnapshot current, double thresholdStdDev = 2.0)
    {
        var alerts = new List<AnomalyAlert>();
        
        if (_history.Count < 3)
            return alerts;

        var baseline = CalculateBaseline();

        // CPU anomaly
        if (current.TotalCpuPercent > baseline.CpuMean + (thresholdStdDev * baseline.CpuStdDev))
        {
            alerts.Add(new AnomalyAlert
            {
                Type = "CPU",
                Severity = current.TotalCpuPercent > baseline.CpuMean + (3 * baseline.CpuStdDev) ? "high" : "medium",
                CurrentValue = current.TotalCpuPercent,
                BaselineMean = baseline.CpuMean,
                BaselineStdDev = baseline.CpuStdDev,
                Deviation = (current.TotalCpuPercent - baseline.CpuMean) / baseline.CpuStdDev
            });
        }

        // Disk anomaly
        if (current.DiskBusyPercent > baseline.DiskMean + (thresholdStdDev * baseline.DiskStdDev))
        {
            alerts.Add(new AnomalyAlert
            {
                Type = "Disk",
                Severity = current.DiskBusyPercent > baseline.DiskMean + (3 * baseline.DiskStdDev) ? "high" : "medium",
                CurrentValue = current.DiskBusyPercent,
                BaselineMean = baseline.DiskMean,
                BaselineStdDev = baseline.DiskStdDev,
                Deviation = (current.DiskBusyPercent - baseline.DiskMean) / baseline.DiskStdDev
            });
        }

        // Memory anomaly (inverse - low memory is bad)
        if (current.AvailableMemoryMB < baseline.MemoryMean - (thresholdStdDev * baseline.MemoryStdDev))
        {
            alerts.Add(new AnomalyAlert
            {
                Type = "Memory",
                Severity = current.AvailableMemoryMB < baseline.MemoryMean - (3 * baseline.MemoryStdDev) ? "high" : "medium",
                CurrentValue = current.AvailableMemoryMB,
                BaselineMean = baseline.MemoryMean,
                BaselineStdDev = baseline.MemoryStdDev,
                Deviation = (baseline.MemoryMean - current.AvailableMemoryMB) / baseline.MemoryStdDev
            });
        }

        return alerts;
    }

    private static void SaveHistory()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, HistoryFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_history, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicThresholdService] Error saving history: {ex.Message}");
        }
    }

    private static void LoadHistory()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, HistoryFileName);
            
            if (!File.Exists(fullPath))
                return;

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<List<SystemHealthSnapshot>>(json);
            if (loaded != null)
            {
                _history.Clear();
                _history.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicThresholdService] Error loading history: {ex.Message}");
        }
    }

    private static void SaveProcessHistories()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, ProcessHistoriesFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_processHistories, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicThresholdService] Error saving process histories: {ex.Message}");
        }
    }

    private static void LoadProcessHistories()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, ProcessHistoriesFileName);
            
            if (!File.Exists(fullPath))
                return;

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ProcessThresholdHistory>>(json);
            if (loaded != null)
            {
                _processHistories.Clear();
                foreach (var kvp in loaded)
                    _processHistories[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicThresholdService] Error loading process histories: {ex.Message}");
        }
    }

    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }
}

public class ThresholdBaseline
{
    public double CpuMean { get; set; }
    public double CpuStdDev { get; set; }
    public double CpuMin { get; set; }
    public double CpuMax { get; set; }
    public double DiskMean { get; set; }
    public double DiskStdDev { get; set; }
    public double DiskMin { get; set; }
    public double DiskMax { get; set; }
    public double MemoryMean { get; set; }
    public double MemoryStdDev { get; set; }
    public double MemoryMin { get; set; }
    public double MemoryMax { get; set; }
    public int SampleCount { get; set; }
}

public class ProcessThresholdHistory
{
    public int ProcessId { get; set; }
    public string? FingerprintHash { get; set; }
    public List<ProcessMetrics> Metrics { get; set; } = new();
    public DateTime LastSeen { get; set; }
}

public class ProcessThresholdBaseline
{
    public int ProcessId { get; set; }
    public string? FingerprintHash { get; set; }
    public double CpuMean { get; set; }
    public double CpuStdDev { get; set; }
    public double CpuMin { get; set; }
    public double CpuMax { get; set; }
    public double MemoryMean { get; set; }
    public double MemoryStdDev { get; set; }
    public double MemoryMin { get; set; }
    public double MemoryMax { get; set; }
    public double ReadMean { get; set; }
    public double ReadStdDev { get; set; }
    public double WriteMean { get; set; }
    public double WriteStdDev { get; set; }
    public double HandleCountMean { get; set; }
    public double HandleCountStdDev { get; set; }
    public double HandleCountMin { get; set; }
    public double HandleCountMax { get; set; }
    public int SampleCount { get; set; }
    public DateTime LastSeen { get; set; }
}

public class AnomalyAlert
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public double CurrentValue { get; set; }
    public double BaselineMean { get; set; }
    public double BaselineStdDev { get; set; }
    public double Deviation { get; set; }
}

public class ProcessAnomalyAlert
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public double CurrentValue { get; set; }
    public double BaselineMean { get; set; }
    public double BaselineStdDev { get; set; }
    public double DeviationMultiplier { get; set; }
}
