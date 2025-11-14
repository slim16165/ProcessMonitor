using System.Diagnostics;
using System.Management;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessMonitorService
{
    private readonly ProcessMonitorConfig _config;
    private readonly CommandAnalyzer _commandAnalyzer;
    private readonly PerformanceCollector _performanceCollector;
    private readonly Dictionary<int, ProcessInfo> _processHistory = new();
    private readonly Dictionary<int, DateTime> _lastCpuCheck = new();

    public ProcessMonitorService(
        ProcessMonitorConfig config,
        CommandAnalyzer commandAnalyzer,
        PerformanceCollector performanceCollector)
    {
        _config = config;
        _commandAnalyzer = commandAnalyzer;
        _performanceCollector = performanceCollector;
    }

    public async Task<List<ProcessInfo>> MonitorProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = new List<ProcessInfo>();
        var systemProcesses = Process.GetProcesses();

        foreach (var process in systemProcesses)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var processInfo = await GetProcessInfoAsync(process);
                if (processInfo != null)
                {
                    processes.Add(processInfo);
                    UpdateProcessHistory(processInfo);
                }
            }
            catch
            {
                // Ignora errori di accesso ai processi
            }
        }

        return processes;
    }

    public List<ProcessInfo> DetectBlockedProcesses()
    {
        var blocked = new List<ProcessInfo>();
        var processes = Process.GetProcesses();

        foreach (var process in processes)
        {
            try
            {
                var processInfo = GetProcessInfo(process);
                if (IsProcessBlocked(processInfo))
                {
                    blocked.Add(processInfo);
                }
            }
            catch
            {
                // Ignora errori
            }
        }

        return blocked;
    }

    public CommandAnalysis AnalyzeCommand(int processId)
    {
        try
        {
            var commandLine = GetCommandLine(processId);
            return _commandAnalyzer.AnalyzeCommand(commandLine);
        }
        catch
        {
            return new CommandAnalysis { CommandLine = "Non disponibile" };
        }
    }

    public ProcessMetrics GetProcessMetrics(int processId)
    {
        return _performanceCollector.GetProcessMetrics(processId);
    }

    private async Task<ProcessInfo?> GetProcessInfoAsync(Process process)
    {
        return await Task.Run(() => GetProcessInfo(process));
    }

    private ProcessInfo GetProcessInfo(Process process)
    {
        try
        {
            var processId = process.Id;
            var commandLine = GetCommandLine(processId);
            var metrics = _performanceCollector.GetProcessMetrics(processId);
            
            var processInfo = new ProcessInfo
            {
                ProcessId = processId,
                ProcessName = process.ProcessName,
                CommandLine = commandLine,
                CpuUsage = metrics.CpuUsage,
                MemoryUsage = process.WorkingSet64,
                IsResponding = process.Responding,
                StartTime = process.StartTime,
                ThreadCount = process.Threads.Count,
                Status = ProcessStatus.Normal
            };

            // Calcola tasso di crescita memoria se abbiamo storico
            if (_processHistory.TryGetValue(processId, out var previous))
            {
                var timeDiff = (DateTime.Now - previous.StartTime).TotalSeconds;
                if (timeDiff > 0)
                {
                    var memoryDiff = processInfo.MemoryUsage - previous.MemoryUsage;
                    processInfo.MemoryGrowthRate = memoryDiff / timeDiff;
                }
            }

            // Determina status
            processInfo.Status = DetermineProcessStatus(processInfo);

            return processInfo;
        }
        catch
        {
            return null!;
        }
    }

    private ProcessStatus DetermineProcessStatus(ProcessInfo processInfo)
    {
        // Processo non risponde
        if (!processInfo.IsResponding)
            return ProcessStatus.Blocked;

        // CPU alta
        if (processInfo.CpuUsage > _config.CpuThreshold)
            return ProcessStatus.HighCpu;

        // Memoria alta
        if (processInfo.MemoryUsage > _config.MemoryThresholdMB * 1024L * 1024)
            return ProcessStatus.HighMemory;

        // CPU = 0 ma processo attivo da più di X minuti (possibile blocco)
        if (processInfo.CpuUsage == 0 && 
            processInfo.Uptime.TotalMinutes > _config.BlockedProcessTimeoutMinutes)
        {
            // Verifica se è un processo sospetto
            if (_config.SuspiciousProcesses.Any(sp => 
                processInfo.ProcessName.Contains(sp, StringComparison.OrdinalIgnoreCase)))
            {
                return ProcessStatus.Suspicious;
            }
        }

        // Memoria in crescita costante (possibile memory leak)
        if (processInfo.MemoryGrowthRate > 100 * 1024 * 1024 / 60) // > 100MB/minuto
            return ProcessStatus.HighMemory;

        return ProcessStatus.Normal;
    }

    private bool IsProcessBlocked(ProcessInfo process)
    {
        // Processo non risponde
        if (!process.IsResponding)
            return true;

        // CPU = 0 ma processo ancora attivo da > X minuti
        if (process.CpuUsage == 0 && 
            process.Uptime.TotalMinutes > _config.BlockedProcessTimeoutMinutes)
        {
            // Solo per processi sospetti
            if (_config.SuspiciousProcesses.Any(sp => 
                process.ProcessName.Contains(sp, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Memoria in crescita costante (> 100MB/minuto)
        if (process.MemoryGrowthRate > 100 * 1024 * 1024 / 60)
            return true;

        return false;
    }

    private string GetCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString() ?? string.Empty;
            }
        }
        catch
        {
            // Ignora errori
        }

        return string.Empty;
    }

    private void UpdateProcessHistory(ProcessInfo processInfo)
    {
        _processHistory[processInfo.ProcessId] = processInfo;
        
        // Rimuovi processi vecchi (più di 1 ora)
        var toRemove = _processHistory
            .Where(kvp => (DateTime.Now - kvp.Value.StartTime).TotalHours > 1)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var pid in toRemove)
        {
            _processHistory.Remove(pid);
        }
    }
}

