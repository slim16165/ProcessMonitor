using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ConsoleSessionManager
{
    private readonly ProcessMonitorConfig _config;

    public ConsoleSessionManager(ProcessMonitorConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Trova sessioni pwsh zombie (CPU=0, thread in WaitReason, nessuna attività)
    /// </summary>
    public List<ZombieConsoleSession> FindZombieSessions()
    {
        var zombieSessions = new List<ZombieConsoleSession>();

        try
        {
            var pwshProcesses = Process.GetProcessesByName("pwsh");
            
            foreach (var process in pwshProcesses)
            {
                try
                {
                    var isZombie = IsZombieSession(process);
                    if (isZombie)
                    {
                        var session = new ZombieConsoleSession
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            StartTime = process.StartTime,
                            Uptime = DateTime.Now - process.StartTime,
                            CommandLine = GetCommandLine(process.Id),
                            CpuUsage = GetCpuUsage(process),
                            MemoryMB = process.WorkingSet64 / (1024.0 * 1024.0),
                            IsResponding = process.Responding,
                            ThreadCount = process.Threads.Count,
                            HasActiveTcpConnections = HasActiveTcpConnections(process.Id),
                            WaitReason = GetThreadWaitReasons(process)
                        };
                        
                        zombieSessions.Add(session);
                    }
                }
                catch
                {
                    // Ignora errori su singoli processi
                }
            }
        }
        catch
        {
            // Errore generale
        }

        return zombieSessions.OrderByDescending(s => s.Uptime).ToList();
    }

    /// <summary>
    /// Verifica se una sessione console è zombie
    /// </summary>
    private bool IsZombieSession(Process process)
    {
        try
        {
            // Criteri per sessione zombie:
            // 1. CPU = 0 o molto bassa (< 1%)
            // 2. Processo attivo da più di X minuti
            // 3. Thread tutti in WaitReason (non in esecuzione)
            // 4. Nessuna connessione TCP attiva (se era in fetch)

            var cpuUsage = GetCpuUsage(process);
            var uptime = DateTime.Now - process.StartTime;
            
            // CPU bassa e processo vecchio
            if (cpuUsage < 1.0 && uptime.TotalMinutes > _config.BlockedProcessTimeoutMinutes)
            {
                // Verifica thread wait reasons
                var waitReasons = GetThreadWaitReasons(process);
                var allWaiting = waitReasons.All(wr => 
                    wr.Contains("Suspended", StringComparison.OrdinalIgnoreCase) ||
                    wr.Contains("DelayExecution", StringComparison.OrdinalIgnoreCase) ||
                    wr.Contains("UserRequest", StringComparison.OrdinalIgnoreCase));

                if (allWaiting)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ottiene i WaitReason dei thread di un processo
    /// </summary>
    private List<string> GetThreadWaitReasons(Process process)
    {
        var waitReasons = new List<string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ThreadWaitReason FROM Win32_Thread WHERE ProcessHandle = {process.Id}");
            
            foreach (ManagementObject obj in searcher.Get())
            {
                var reason = obj["ThreadWaitReason"]?.ToString() ?? "Unknown";
                waitReasons.Add(reason);
            }
        }
        catch
        {
            // Ignora errori
        }

        return waitReasons;
    }

    /// <summary>
    /// Verifica se ci sono connessioni TCP attive
    /// </summary>
    private bool HasActiveTcpConnections(int processId)
    {
        try
        {
            // Nota: Per associare connessioni TCP a processi serve netstat
            // Questo è un'approssimazione basata su Get-NetTCPConnection in PowerShell
            var psScript = $@"
                $connections = Get-NetTCPConnection -ErrorAction SilentlyContinue | 
                    Where-Object {{ $_.OwningProcess -eq {processId} }}
                if ($connections) {{ Write-Output 'true' }} else {{ Write-Output 'false' }}
            ";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Chiude sessioni pwsh vecchie
    /// </summary>
    public int CloseOldSessions(TimeSpan maxAge, bool force = false)
    {
        var closedCount = 0;

        try
        {
            var pwshProcesses = Process.GetProcessesByName("pwsh");
            var cutoffTime = DateTime.Now - maxAge;

            foreach (var process in pwshProcesses)
            {
                try
                {
                    if (process.StartTime < cutoffTime)
                    {
                        var isZombie = IsZombieSession(process);
                        
                        if (isZombie || force)
                        {
                            if (force)
                            {
                                process.Kill();
                            }
                            else
                            {
                                process.CloseMainWindow();
                                if (!process.WaitForExit(5000))
                                {
                                    process.Kill();
                                }
                            }
                            
                            closedCount++;
                        }
                    }
                }
                catch
                {
                    // Ignora errori
                }
            }
        }
        catch
        {
            // Errore generale
        }

        return closedCount;
    }

    /// <summary>
    /// Chiude TGitCache.exe (TortoiseGit cache)
    /// </summary>
    public int CloseTGitCache()
    {
        var closedCount = 0;

        try
        {
            var tgitCacheProcesses = Process.GetProcessesByName("TGitCache");

            foreach (var process in tgitCacheProcesses)
            {
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                    }
                    closedCount++;
                }
                catch
                {
                    // Ignora errori
                }
            }
        }
        catch
        {
            // Errore
        }

        return closedCount;
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
}

