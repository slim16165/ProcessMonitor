using System.Diagnostics;
using System.Management;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class PerformanceCollector
{
    private readonly Dictionary<int, ProcessMetrics> _previousMetrics = new();
    private readonly ProcessMonitorConfig _config;

    public PerformanceCollector(ProcessMonitorConfig config)
    {
        _config = config;
    }

    public async Task<CpuMetrics> CollectCpuMetricsAsync()
    {
        return await Task.Run(() =>
        {
            var metrics = new CpuMetrics();
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    var cpuUsage = GetCpuUsage(process);
                    if (cpuUsage.HasValue)
                    {
                        metrics.ProcessCpuUsage[process.Id] = cpuUsage.Value;
                    }
                }
                catch
                {
                    // Ignora errori
                }
            }

            metrics.TotalCpuUsage = metrics.ProcessCpuUsage.Values.Sum();
            return metrics;
        });
    }

    public async Task<MemoryMetrics> CollectMemoryMetricsAsync()
    {
        return await Task.Run(() =>
        {
            var metrics = new MemoryMetrics();
            var processes = Process.GetProcesses();

            foreach (var process in processes)
            {
                try
                {
                    var memory = process.WorkingSet64;
                    metrics.ProcessMemoryUsage[process.Id] = memory;
                    metrics.TotalMemoryBytes += memory;
                }
                catch
                {
                    // Ignora errori
                }
            }

            // Ottieni memoria disponibile
            var pc = new PerformanceCounter("Memory", "Available Bytes");
            metrics.AvailableMemoryBytes = (long)pc.NextValue();
            
            return metrics;
        });
    }

    public async Task<IoMetrics> CollectIoMetricsAsync(int processId)
    {
        return await Task.Run(() =>
        {
            var metrics = new IoMetrics { ProcessId = processId };
            
            try
            {
                var process = Process.GetProcessById(processId);
                
                // Usa PerformanceCounter per I/O
                var readCounter = new PerformanceCounter("Process", "IO Data Bytes/sec", process.ProcessName);
                var writeCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", process.ProcessName);
                
                metrics.ReadBytesPerSecond = readCounter.NextValue();
                metrics.WriteBytesPerSecond = writeCounter.NextValue();
                
                // Calcola bytes totali se abbiamo metriche precedenti
                if (_previousMetrics.TryGetValue(processId, out var previous))
                {
                    var timeDiff = (DateTime.Now - previous.Timestamp).TotalSeconds;
                    if (timeDiff > 0)
                    {
                        metrics.ReadBytes = previous.IoReadBytes + (long)(metrics.ReadBytesPerSecond * timeDiff);
                        metrics.WriteBytes = previous.IoWriteBytes + (long)(metrics.WriteBytesPerSecond * timeDiff);
                    }
                }
                
                // Aggiorna cache
                _previousMetrics[processId] = new ProcessMetrics
                {
                    ProcessId = processId,
                    IoReadBytes = metrics.ReadBytes,
                    IoWriteBytes = metrics.WriteBytes,
                    IoReadBytesPerSecond = metrics.ReadBytesPerSecond,
                    IoWriteBytesPerSecond = metrics.WriteBytesPerSecond
                };
            }
            catch
            {
                // Ignora errori
            }
            
            return metrics;
        });
    }

    public ProcessMetrics GetProcessMetrics(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var cpuUsage = GetCpuUsage(process);
            
            return new ProcessMetrics
            {
                ProcessId = processId,
                CpuUsage = cpuUsage ?? 0,
                MemoryUsage = process.WorkingSet64,
                Timestamp = DateTime.Now
            };
        }
        catch
        {
            return new ProcessMetrics { ProcessId = processId };
        }
    }

    private double? GetCpuUsage(Process process)
    {
        try
        {
            // Usa PerformanceCounter per CPU
            var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);
            cpuCounter.NextValue(); // Prima chiamata restituisce 0
            Thread.Sleep(100); // Attendi un po' per ottenere un valore accurato
            return cpuCounter.NextValue() / Environment.ProcessorCount;
        }
        catch
        {
            return null;
        }
    }
}

