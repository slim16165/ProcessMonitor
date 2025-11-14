using System.Diagnostics;
using System.Management;
using System.Text;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ConsoleProcessMonitor
{
    private readonly Dictionary<int, ProcessCommandInfo> _processCommands = new();
    private readonly ProcessMonitorConfig _config;

    public ConsoleProcessMonitor(ProcessMonitorConfig config)
    {
        _config = config;
    }

    public List<ProcessCommandInfo> GetConsoleAndGitProcesses()
    {
        var processes = new List<ProcessCommandInfo>();
        var allProcesses = Process.GetProcesses();

        foreach (var process in allProcesses)
        {
            try
            {
                var processName = process.ProcessName.ToLowerInvariant();
                
                // Filtra solo processi console e git
                if (IsConsoleOrGitProcess(processName))
                {
                    var commandInfo = GetProcessCommandInfo(process);
                    if (commandInfo != null)
                    {
                        processes.Add(commandInfo);
                        _processCommands[process.Id] = commandInfo;
                    }
                }
            }
            catch
            {
                // Ignora errori di accesso
            }
        }

        return processes.OrderByDescending(p => p.StartTime).ToList();
    }

    private bool IsConsoleOrGitProcess(string processName)
    {
        var consoleProcesses = new[] { "cmd", "powershell", "pwsh", "bash", "git", "git.exe", 
            "python", "node", "nodejs", "dotnet", "devenv", "code", "cursor" };
        
        return consoleProcesses.Any(p => processName.Contains(p));
    }

    private ProcessCommandInfo? GetProcessCommandInfo(Process process)
    {
        try
        {
            var commandLine = GetCommandLine(process.Id);
            var analysis = AnalyzeCommand(commandLine);
            
            return new ProcessCommandInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                CommandLine = commandLine,
                StartTime = process.StartTime,
                CpuUsage = GetCpuUsage(process),
                MemoryMB = process.WorkingSet64 / (1024.0 * 1024.0),
                IsResponding = process.Responding,
                ThreadCount = process.Threads.Count,
                Status = DetermineStatus(process, analysis),
                CommandAnalysis = analysis,
                Uptime = DateTime.Now - process.StartTime,
                ParentProcessId = GetParentProcessId(process.Id),
                ParentProcessName = GetParentProcessName(process.Id)
            };
        }
        catch
        {
            return null;
        }
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

    private int GetParentProcessId(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
            
            foreach (ManagementObject obj in searcher.Get())
            {
                return Convert.ToInt32(obj["ParentProcessId"]);
            }
        }
        catch
        {
            // Ignora errori
        }

        return 0;
    }

    private string GetParentProcessName(int processId)
    {
        try
        {
            var parentId = GetParentProcessId(processId);
            if (parentId > 0)
            {
                var parent = Process.GetProcessById(parentId);
                return parent.ProcessName;
            }
        }
        catch
        {
            // Ignora errori
        }

        return "N/A";
    }

    private double GetCpuUsage(Process process)
    {
        try
        {
            var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);
            cpuCounter.NextValue(); // Prima chiamata restituisce 0
            Thread.Sleep(50);
            return cpuCounter.NextValue() / Environment.ProcessorCount;
        }
        catch
        {
            return 0;
        }
    }

    private CommandAnalysis AnalyzeCommand(string commandLine)
    {
        var analysis = new CommandAnalysis { CommandLine = commandLine };
        
        if (string.IsNullOrWhiteSpace(commandLine))
            return analysis;

        var cmdLower = commandLine.ToLowerInvariant();

        // Analisi Git
        if (cmdLower.Contains("git"))
        {
            if (cmdLower.Contains("diff"))
            {
                analysis.Warnings.Add("Git diff in esecuzione");
                if (cmdLower.Contains("--name-status"))
                    analysis.Warnings.Add("Diff su molti file");
            }
            else if (cmdLower.Contains("cat-file"))
            {
                analysis.RiskLevel = RiskLevel.Low;
            }
            else if (cmdLower.Contains("push") || cmdLower.Contains("pull"))
            {
                analysis.Warnings.Add("Operazione di rete Git");
            }
        }

        // Analisi PowerShell
        if (cmdLower.Contains("powershell") || cmdLower.Contains("pwsh"))
        {
            if (cmdLower.Contains("-recurse") && !cmdLower.Contains("-depth"))
                analysis.Warnings.Add("Ricerca ricorsiva senza limite");
            
            if (cmdLower.Contains("get-childitem") && cmdLower.Contains("c:\\"))
                analysis.Warnings.Add("Ricerca su drive root");
        }

        // Analisi Python
        if (cmdLower.Contains("python"))
        {
            if (cmdLower.Contains("subprocess") && !cmdLower.Contains("timeout"))
                analysis.Warnings.Add("Subprocess senza timeout");
        }

        if (analysis.Warnings.Any())
            analysis.RiskLevel = RiskLevel.Medium;

        return analysis;
    }

    private ProcessStatus DetermineStatus(Process process, CommandAnalysis analysis)
    {
        if (!process.Responding)
            return ProcessStatus.Blocked;

        var cpuUsage = GetCpuUsage(process);
        if (cpuUsage > _config.CpuThreshold)
            return ProcessStatus.HighCpu;

        var memoryMB = process.WorkingSet64 / (1024.0 * 1024.0);
        if (memoryMB > _config.MemoryThresholdMB)
            return ProcessStatus.HighMemory;

        if (analysis.RiskLevel >= RiskLevel.High)
            return ProcessStatus.Suspicious;

        return ProcessStatus.Normal;
    }
}

public class ProcessCommandInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public bool IsResponding { get; set; }
    public int ThreadCount { get; set; }
    public ProcessStatus Status { get; set; }
    public CommandAnalysis CommandAnalysis { get; set; } = new();
    public int ParentProcessId { get; set; }
    public string ParentProcessName { get; set; } = string.Empty;
    
    public string ShortCommandLine => CommandLine.Length > 80 
        ? CommandLine.Substring(0, 77) + "..." 
        : CommandLine;
    
    public string StatusIcon => Status switch
    {
        ProcessStatus.Blocked => "🔴",
        ProcessStatus.HighCpu => "🟠",
        ProcessStatus.HighMemory => "🟡",
        ProcessStatus.Suspicious => "⚠️",
        _ => "🟢"
    };
    
    public string RiskIcon => CommandAnalysis.RiskLevel switch
    {
        RiskLevel.Critical => "🔴",
        RiskLevel.High => "🟠",
        RiskLevel.Medium => "🟡",
        _ => "🟢"
    };
}

