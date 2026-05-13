using System.Diagnostics;
using System.Management;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessSnapshotService
{
    private readonly ProcessMonitorConfig _config;
    private readonly CommandAnalyzer _commandAnalyzer;

    public ProcessSnapshotService(ProcessMonitorConfig config, CommandAnalyzer commandAnalyzer)
    {
        _config = config;
        _commandAnalyzer = commandAnalyzer;
    }

    public List<ProcessTreeNode> CaptureSnapshot()
    {
        var tcpActivity = GetTcpOwningProcesses();
        var cpuByPid = GetCpuByPid();
        var runtimeProcesses = Process.GetProcesses().ToDictionary(p => p.Id);
        var nodes = new List<ProcessTreeNode>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate, SessionId, WorkingSetSize FROM Win32_Process");

        foreach (ManagementObject obj in searcher.Get())
        {
            try
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                runtimeProcesses.TryGetValue(pid, out var process);

                var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                var processName = obj["Name"]?.ToString() ?? process?.ProcessName ?? string.Empty;
                var analysis = _commandAnalyzer.AnalyzeCommand(commandLine);
                var cpuUsage = cpuByPid.TryGetValue(pid, out var cpu) ? cpu : 0;
                var workingSetBytes = obj["WorkingSetSize"] != null ? Convert.ToInt64(obj["WorkingSetSize"]) : process?.WorkingSet64 ?? 0;
                var memoryMb = workingSetBytes / (1024.0 * 1024.0);
                var startTime = TryGetStartTime(process, obj["CreationDate"]?.ToString());
                var parentPid = obj["ParentProcessId"] != null ? Convert.ToInt32(obj["ParentProcessId"]) : 0;
                var isResponding = process?.Responding ?? true;
                var threadCount = process?.Threads.Count ?? 0;

                nodes.Add(new ProcessTreeNode
                {
                    ProcessId = pid,
                    ParentProcessId = parentPid > 0 ? parentPid : null,
                    ProcessName = processName,
                    CommandLine = commandLine,
                    ExecutablePath = obj["ExecutablePath"]?.ToString(),
                    WorkingDirectory = ExtractWorkingDirectory(commandLine, obj["ExecutablePath"]?.ToString()),
                    Owner = null,
                    SessionId = obj["SessionId"] != null ? Convert.ToInt32(obj["SessionId"]) : null,
                    StartTime = startTime,
                    CpuUsage = cpuUsage,
                    MemoryMB = memoryMb,
                    IsResponding = isResponding,
                    ThreadCount = threadCount,
                    ReadBytesPerSecond = 0,
                    WriteBytesPerSecond = 0,
                    HasTcpActivity = tcpActivity.Contains(pid),
                    CommandAnalysis = analysis,
                    Status = DetermineStatus(isResponding, cpuUsage, memoryMb, analysis.RiskLevel),
                    LaunchCategory = ProcessMonitorClassifier.CategorizeLaunch(processName, commandLine)
                });
            }
            catch
            {
                // Ignore inaccessible or short-lived processes.
            }
        }

        var byPid = nodes.ToDictionary(n => n.ProcessId);
        foreach (var node in nodes)
        {
            if (node.ParentProcessId.HasValue && byPid.TryGetValue(node.ParentProcessId.Value, out var parent))
            {
                node.ParentStartTime = parent.StartTime;
            }
        }

        return nodes;
    }

    private static Dictionary<int, double> GetCpuByPid()
    {
        var result = new Dictionary<int, double>();

        try
        {
            using var cpuSearcher = new ManagementObjectSearcher(
                "SELECT IDProcess, PercentProcessorTime FROM Win32_PerfFormattedData_PerfProc_Process");

            foreach (ManagementObject obj in cpuSearcher.Get())
            {
                try
                {
                    var pid = Convert.ToInt32(obj["IDProcess"]);
                    var cpu = Convert.ToDouble(obj["PercentProcessorTime"]) / Environment.ProcessorCount;
                    result[pid] = cpu;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static DateTime? TryGetStartTime(Process? process, string? wmiCreationDate)
    {
        try
        {
            return process?.StartTime;
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(wmiCreationDate))
                return null;

            try
            {
                return ManagementDateTimeConverter.ToDateTime(wmiCreationDate);
            }
            catch
            {
                return null;
            }
        }
    }

    private HashSet<int> GetTcpOwningProcesses()
    {
        var result = new HashSet<int>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p tcp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return result;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid))
                {
                    result.Add(pid);
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static string? ExtractWorkingDirectory(string commandLine, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                commandLine,
                @"-C\s+[""']?([^""'\r\n]+)[""']?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var candidate = match.Groups[1].Value.Trim();
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
            return Path.GetDirectoryName(executablePath);

        return null;
    }

    private ProcessStatus DetermineStatus(bool isResponding, double cpuUsage, double memoryMb, RiskLevel riskLevel)
    {
        if (!isResponding)
            return ProcessStatus.Blocked;
        if (cpuUsage > _config.CpuThreshold)
            return ProcessStatus.HighCpu;
        if (memoryMb > _config.MemoryThresholdMB)
            return ProcessStatus.HighMemory;
        if (riskLevel >= RiskLevel.High)
            return ProcessStatus.Suspicious;
        return ProcessStatus.Normal;
    }
}
