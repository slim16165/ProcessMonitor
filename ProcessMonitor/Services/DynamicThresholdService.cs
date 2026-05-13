using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class DynamicThresholdService
{
    private static readonly List<SystemHealthSnapshot> _history = new();
    private const int MaxHistorySize = 10;
    private const string HistoryFileName = "threshold_history.json";

    static DynamicThresholdService()
    {
        LoadHistory();
    }

    public static void AddSnapshot(SystemHealthSnapshot snapshot)
    {
        _history.Add(snapshot);
        if (_history.Count > MaxHistorySize)
            _history.RemoveAt(0);
        
        SaveHistory();
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

public class AnomalyAlert
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public double CurrentValue { get; set; }
    public double BaselineMean { get; set; }
    public double BaselineStdDev { get; set; }
    public double Deviation { get; set; }
}
