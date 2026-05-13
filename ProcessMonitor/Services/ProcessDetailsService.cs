using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessDetailsService
{
    /// <summary>
    /// Ottiene la working directory di un processo
    /// </summary>
    public string? GetWorkingDirectory(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}");
            
            foreach (ManagementObject obj in searcher.Get())
            {
                var executablePath = obj["ExecutablePath"]?.ToString();
                if (!string.IsNullOrEmpty(executablePath))
                {
                    // Per alcuni processi, la working directory è la directory dell'eseguibile
                    return Path.GetDirectoryName(executablePath);
                }
            }

            // Metodo alternativo: usa WMI per ottenere la working directory
            using var searcher2 = new ManagementObjectSearcher(
                $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            
            foreach (ManagementObject obj in searcher2.Get())
            {
                var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                
                // Estrai working directory dal comando git
                if (commandLine.Contains("git", StringComparison.OrdinalIgnoreCase))
                {
                    // Pattern: git -C "path" command
                    var match = System.Text.RegularExpressions.Regex.Match(
                        commandLine, 
                        @"-C\s+[""']?([^""'\s]+)[""']?",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var path = match.Groups[1].Value;
                        if (Directory.Exists(path))
                        {
                            return path;
                        }
                    }

                    // Pattern: cerca path dopo "git" che sia una directory esistente
                    var parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var cleanPart = part.Trim('"', '\'');
                        if (Directory.Exists(cleanPart) && !cleanPart.Contains("git"))
                        {
                            return cleanPart;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignora errori
        }

        return null;
    }

    /// <summary>
    /// Ottiene informazioni dettagliate su I/O file di un processo
    /// </summary>
    public async Task<ProcessIoInfo> GetProcessIoInfoAsync(int processId)
    {
        var ioInfo = new ProcessIoInfo { ProcessId = processId };

        try
        {
            var process = Process.GetProcessById(processId);
            
            // Usa Performance Counters per I/O
            try
            {
                var readCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", process.ProcessName);
                var writeCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", process.ProcessName);
                
                readCounter.NextValue(); // Prima chiamata restituisce 0
                writeCounter.NextValue();
                
                await Task.Delay(100); // Attendi per ottenere valori accurati
                
                ioInfo.ReadBytesPerSecond = (long)readCounter.NextValue();
                ioInfo.WriteBytesPerSecond = (long)writeCounter.NextValue();
            }
            catch
            {
                // Performance counters potrebbero non essere disponibili
            }

            // Prova a ottenere file aperti (richiede privilegi elevati)
            try
            {
                ioInfo.OpenFiles = GetOpenFiles(processId);
            }
            catch
            {
                // Richiede privilegi elevati o tool esterni
            }
        }
        catch
        {
            // Processo non trovato o errore
        }

        return ioInfo;
    }

    /// <summary>
    /// Ottiene la lista di file aperti da un processo (approssimativo)
    /// </summary>
    private List<string> GetOpenFiles(int processId)
    {
        var files = new List<string>();
        
        // Nota: Ottenere file aperti richiede tool esterni come Handle.exe o Process Explorer
        // Questo è un placeholder - l'implementazione completa richiederebbe Handle.exe
        
        return files;
    }

    /// <summary>
    /// Verifica se un processo è bloccato su I/O
    /// </summary>
    public async Task<bool> IsProcessBlockedOnIoAsync(int processId)
    {
        try
        {
            var ioInfo = await GetProcessIoInfoAsync(processId);
            
            // Se I/O è alto ma CPU è bassa, potrebbe essere bloccato su I/O
            var process = Process.GetProcessById(processId);
            var cpuUsage = GetCpuUsage(process);
            
            // I/O > 10MB/s ma CPU < 5% potrebbe indicare blocco I/O
            var ioMBps = (ioInfo.ReadBytesPerSecond + ioInfo.WriteBytesPerSecond) / (1024.0 * 1024.0);
            
            return ioMBps > 10 && cpuUsage < 5;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ottiene connessioni TCP attive per un processo
    /// </summary>
    public List<TcpConnectionInfo> GetTcpConnections(int processId)
    {
        var connections = new List<TcpConnectionInfo>();

        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();

            foreach (var connection in tcpConnections)
            {
                // Nota: Per associare connessioni TCP a processi serve netstat o tool esterni
                // Questo è un placeholder - l'implementazione completa richiederebbe netstat
            }
        }
        catch
        {
            // Errore
        }

        return connections;
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

    /// <summary>
    /// Ottiene informazioni complete su un processo Git
    /// </summary>
    public async Task<GitProcessDetails> GetGitProcessDetailsAsync(int processId)
    {
        var details = new GitProcessDetails
        {
            ProcessId = processId
        };

        try
        {
            var process = Process.GetProcessById(processId);
            details.ProcessName = process.ProcessName;
            details.CommandLine = GetCommandLine(processId);
            details.WorkingDirectory = GetWorkingDirectory(processId);
            details.StartTime = process.StartTime;
            details.CpuUsage = GetCpuUsage(process);
            details.MemoryMB = process.WorkingSet64 / (1024.0 * 1024.0);
            details.IsResponding = process.Responding;
            
            var ioInfo = await GetProcessIoInfoAsync(processId);
            details.IoInfo = ioInfo;
            details.IsBlockedOnIo = await IsProcessBlockedOnIoAsync(processId);
            
            // Estrai informazioni Git dal comando
            if (!string.IsNullOrEmpty(details.CommandLine))
            {
                details.GitCommand = ExtractGitCommand(details.CommandLine);
                details.GitRepository = ExtractGitRepository(details.CommandLine, details.WorkingDirectory);
            }
        }
        catch
        {
            // Errore
        }

        return details;
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

    private string? ExtractGitCommand(string commandLine)
    {
        // Estrai il comando git (es: "git fetch", "git diff", ecc.)
        var match = System.Text.RegularExpressions.Regex.Match(
            commandLine,
            @"git\s+(\w+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        return match.Success && match.Groups.Count > 1 
            ? match.Groups[1].Value 
            : null;
    }

    private string? ExtractGitRepository(string commandLine, string? workingDirectory)
    {
        // Prova a estrarre il repository dal comando
        var match = System.Text.RegularExpressions.Regex.Match(
            commandLine,
            @"-C\s+[""']?([^""'\s]+)[""']?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success && match.Groups.Count > 1)
        {
            var path = match.Groups[1].Value;
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Se non trovato nel comando, usa working directory
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            // Verifica se è un repository Git
            var gitDir = Path.Combine(workingDirectory, ".git");
            if (Directory.Exists(gitDir))
            {
                return workingDirectory;
            }
        }

        return null;
    }
}

