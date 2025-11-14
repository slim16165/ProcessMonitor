using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProcessMonitor.Models;
using ProcessMonitor.Services;

namespace ProcessMonitor;

class Program
{
    private static ILogger<Program>? _logger;

    static async Task Main(string[] args)
    {
        // Configurazione
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<Program>();

        // Carica configurazione
        var monitorConfig = new ProcessMonitorConfig();
        configuration.GetSection("ProcessMonitor").Bind(monitorConfig);

        var notificationConfig = new NotificationConfig();
        configuration.GetSection("Notifications").Bind(notificationConfig);

        var externalToolsConfig = new ExternalToolsConfig();
        configuration.GetSection("ExternalTools").Bind(externalToolsConfig);

        // Inizializza servizi
        var commandAnalyzer = new CommandAnalyzer(monitorConfig);
        var performanceCollector = new PerformanceCollector(monitorConfig);
        var processMonitor = new ProcessMonitorService(monitorConfig, commandAnalyzer, performanceCollector);
        var directoryAnalyzer = new DirectoryAnalyzer(monitorConfig);
        var externalTools = new ExternalToolsService(externalToolsConfig, loggerFactory.CreateLogger<ExternalToolsService>());
        var consoleProcessMonitor = new ConsoleProcessMonitor(monitorConfig);

        _logger.LogInformation("=== Process Monitor Avviato ===");
        _logger.LogInformation("Comandi disponibili:");
        _logger.LogInformation("  'q' - Esci");
        _logger.LogInformation("  's' - Analisi workspace");
        _logger.LogInformation("  'b' - Processi bloccati");
        _logger.LogInformation("  'a' - Tutti i processi");
        _logger.LogInformation("  'w' - Analizza con WhatIsHang (richiede PID)");
        _logger.LogInformation("  'u' - Analizza con UIHang (richiede PID)");
        _logger.LogInformation("  'e' - Apri Process Explorer (richiede PID)");
        _logger.LogInformation("  'p' - Apri Procmon");
        _logger.LogInformation("  't' - Lista strumenti disponibili");
        _logger.LogInformation("  'c' - Monitor Console/Git (tabella grafica)");

        var cancellationTokenSource = new CancellationTokenSource();

        // Task di monitoraggio continuo
        var monitorTask = Task.Run(async () =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var blocked = processMonitor.DetectBlockedProcesses();
                    
                    if (blocked.Any())
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n⚠️  Trovati {blocked.Count} processi bloccati/sospetti:");
                        Console.ResetColor();

                        foreach (var proc in blocked)
                        {
                            var analysis = processMonitor.AnalyzeCommand(proc.ProcessId);
                            
                            Console.WriteLine($"\n  PID: {proc.ProcessId}");
                            Console.WriteLine($"  Nome: {proc.ProcessName}");
                            Console.WriteLine($"  Status: {proc.Status}");
                            Console.WriteLine($"  CPU: {proc.CpuUsage:F2}%");
                            Console.WriteLine($"  Memoria: {proc.MemoryUsage / (1024 * 1024):F2} MB");
                            Console.WriteLine($"  Uptime: {proc.Uptime.TotalMinutes:F1} minuti");
                            Console.WriteLine($"  Risponde: {(proc.IsResponding ? "Sì" : "No")}");
                            
                            if (!string.IsNullOrEmpty(proc.CommandLine))
                            {
                                Console.WriteLine($"  Comando: {proc.CommandLine.Substring(0, Math.Min(100, proc.CommandLine.Length))}...");
                            }

                            if (analysis.RiskLevel >= RiskLevel.High)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"  ⚠️  Livello di rischio: {analysis.RiskLevel}");
                                foreach (var warning in analysis.Warnings)
                                {
                                    Console.WriteLine($"     - {warning}");
                                }
                                Console.ResetColor();
                            }

                            // Auto-analisi se configurato
                            if (externalToolsConfig.AutoAnalyzeBlockedProcesses)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"  🔍 Analisi automatica con {externalToolsConfig.PreferredTool}...");
                                Console.ResetColor();
                                
                                AnalyzeWithPreferredTool(externalTools, proc.ProcessId, externalToolsConfig.PreferredTool);
                            }
                        }
                    }

                    await Task.Delay(monitorConfig.CheckIntervalSeconds * 1000, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore durante il monitoraggio");
                }
            }
        }, cancellationTokenSource.Token);

        // Loop principale per comandi interattivi
        while (!cancellationTokenSource.Token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                
                if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                {
                    _logger.LogInformation("Uscita richiesta dall'utente");
                    cancellationTokenSource.Cancel();
                    break;
                }
                else if (key.KeyChar == 's' || key.KeyChar == 'S')
                {
                    await AnalyzeWorkspace(directoryAnalyzer);
                }
                else if (key.KeyChar == 'b' || key.KeyChar == 'B')
                {
                    await ShowBlockedProcesses(processMonitor, externalTools, externalToolsConfig);
                }
                else if (key.KeyChar == 'a' || key.KeyChar == 'A')
                {
                    await ShowAllProcesses(processMonitor);
                }
                else if (key.KeyChar == 'w' || key.KeyChar == 'W')
                {
                    await AnalyzeWithWhatIsHang(externalTools);
                }
                else if (key.KeyChar == 'u' || key.KeyChar == 'U')
                {
                    await AnalyzeWithUIHang(externalTools);
                }
                else if (key.KeyChar == 'e' || key.KeyChar == 'E')
                {
                    await OpenProcessExplorer(externalTools);
                }
                else if (key.KeyChar == 'p' || key.KeyChar == 'P')
                {
                    OpenProcmon(externalTools);
                }
                else if (key.KeyChar == 't' || key.KeyChar == 'T')
                {
                    ShowAvailableTools(externalTools);
                }
                else if (key.KeyChar == 'c' || key.KeyChar == 'C')
                {
                    ShowConsoleGitMonitor(consoleProcessMonitor);
                }
            }

            await Task.Delay(100, cancellationTokenSource.Token);
        }

        await monitorTask;
        _logger.LogInformation("Process Monitor terminato");
    }

    private static async Task AnalyzeWorkspace(DirectoryAnalyzer analyzer)
    {
        Console.WriteLine("\n=== Analisi Workspace ===");
        var workspacePath = Directory.GetCurrentDirectory();
        Console.WriteLine($"Path: {workspacePath}");

        try
        {
            var analysis = await analyzer.AnalyzeDirectoryAsync(workspacePath);
            
            Console.WriteLine($"File trovati: {analysis.FileCount:N0}");
            Console.WriteLine($"Dimensione totale: {analysis.TotalSize / (1024.0 * 1024):F2} MB");
            Console.WriteLine($"Profondità massima: {analysis.MaxDepth}");

            if (!analysis.AnalysisCompleted)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️  {analysis.TimeoutReason}");
                Console.ResetColor();
            }

            if (analysis.LargeDirectories.Any())
            {
                Console.WriteLine("\nDirectory grandi trovate:");
                foreach (var dir in analysis.LargeDirectories.Take(10))
                {
                    Console.WriteLine($"  - {dir}");
                }
            }

            // Trova directory problematiche
            var problematic = analyzer.FindProblematicDirectories(workspacePath);
            if (problematic.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n⚠️  Directory problematiche:");
                Console.ResetColor();
                foreach (var dir in problematic.Take(5))
                {
                    Console.WriteLine($"  - {dir.Path}");
                    Console.WriteLine($"    {dir.Reason}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Errore durante l'analisi: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static async Task ShowBlockedProcesses(
        ProcessMonitorService monitor, 
        ExternalToolsService externalTools,
        ExternalToolsConfig externalToolsConfig)
    {
        Console.WriteLine("\n=== Processi Bloccati ===");
        var blocked = monitor.DetectBlockedProcesses();
        
        if (!blocked.Any())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Nessun processo bloccato trovato");
            Console.ResetColor();
            return;
        }

        foreach (var proc in blocked)
        {
            var analysis = monitor.AnalyzeCommand(proc.ProcessId);
            
            Console.WriteLine($"\nPID: {proc.ProcessId} - {proc.ProcessName}");
            Console.WriteLine($"  Status: {proc.Status}");
            Console.WriteLine($"  CPU: {proc.CpuUsage:F2}%");
            Console.WriteLine($"  Memoria: {proc.MemoryUsage / (1024 * 1024):F2} MB");
            
            if (analysis.RiskLevel >= RiskLevel.Medium)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Rischio: {analysis.RiskLevel}");
                foreach (var warning in analysis.Warnings)
                {
                    Console.WriteLine($"    - {warning}");
                }
                Console.ResetColor();
            }

            Console.WriteLine($"\n  Premi 'w' per analizzare con WhatIsHang, 'u' per UIHang, 'e' per Process Explorer");
        }
    }

    private static async Task ShowAllProcesses(ProcessMonitorService monitor)
    {
        Console.WriteLine("\n=== Tutti i Processi ===");
        var processes = await monitor.MonitorProcessesAsync(CancellationToken.None);
        
        var suspicious = processes
            .Where(p => _logger != null && 
                (p.Status != ProcessStatus.Normal || 
                 monitor.GetProcessMetrics(p.ProcessId).CpuUsage > 5))
            .OrderByDescending(p => p.CpuUsage)
            .Take(20)
            .ToList();

        Console.WriteLine($"Mostrando top {suspicious.Count} processi:");
        Console.WriteLine($"{"PID",-8} {"Nome",-20} {"CPU%",-8} {"Memoria MB",-12} {"Status",-12}");
        Console.WriteLine(new string('-', 70));

        foreach (var proc in suspicious)
        {
            Console.WriteLine($"{proc.ProcessId,-8} {proc.ProcessName,-20} {proc.CpuUsage,-8:F2} " +
                            $"{proc.MemoryUsage / (1024 * 1024),-12:F2} {proc.Status,-12}");
        }
    }

    private static async Task AnalyzeWithWhatIsHang(ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo da analizzare: ");
        if (int.TryParse(Console.ReadLine(), out var pid))
        {
            var result = externalTools.AnalyzeWithWhatIsHang(pid);
            if (result)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("WhatIsHang avviato con successo");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Errore nell'avvio di WhatIsHang. Verifica il percorso in appsettings.json");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PID non valido");
            Console.ResetColor();
        }
    }

    private static async Task AnalyzeWithUIHang(ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo da analizzare: ");
        if (int.TryParse(Console.ReadLine(), out var pid))
        {
            var result = externalTools.AnalyzeWithUIHang(pid);
            if (result)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("UIHang avviato con successo");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Errore nell'avvio di UIHang. Verifica il percorso in appsettings.json");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PID non valido");
            Console.ResetColor();
        }
    }

    private static async Task OpenProcessExplorer(ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo (opzionale, lascia vuoto per aprire senza filtro): ");
        var input = Console.ReadLine();
        
        bool result;
        if (string.IsNullOrWhiteSpace(input))
        {
            // Process Explorer senza PID - apri normalmente
            result = externalTools.OpenWithProcessExplorer(-1);
        }
        else if (int.TryParse(input, out var pid))
        {
            result = externalTools.OpenWithProcessExplorer(pid);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PID non valido");
            Console.ResetColor();
            return;
        }

        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Process Explorer avviato con successo");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Errore nell'avvio di Process Explorer. Verifica il percorso in appsettings.json");
            Console.ResetColor();
        }
    }

    private static void OpenProcmon(ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo (opzionale, lascia vuoto per aprire senza filtro): ");
        var input = Console.ReadLine();
        
        int? pid = null;
        if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out var parsedPid))
        {
            pid = parsedPid;
        }

        var result = externalTools.OpenWithProcmon(pid);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Procmon avviato con successo");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Errore nell'avvio di Procmon. Verifica il percorso in appsettings.json");
            Console.ResetColor();
        }
    }

    private static void ShowAvailableTools(ExternalToolsService externalTools)
    {
        Console.WriteLine("\n=== Strumenti Esterni Disponibili ===");
        var tools = externalTools.GetAvailableTools();
        
        if (!tools.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Nessuno strumento esterno configurato o trovato.");
            Console.WriteLine("Configura i percorsi in appsettings.json");
            Console.ResetColor();
            return;
        }

        foreach (var tool in tools)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ {tool.Name}");
            Console.ResetColor();
            Console.WriteLine($"  Path: {tool.Path}");
            Console.WriteLine($"  Descrizione: {tool.Description}");
            Console.WriteLine();
        }
    }

    private static void AnalyzeWithPreferredTool(ExternalToolsService externalTools, int processId, string preferredTool)
    {
        var success = preferredTool.ToLowerInvariant() switch
        {
            "whatishang" => externalTools.AnalyzeWithWhatIsHang(processId),
            "uihang" => externalTools.AnalyzeWithUIHang(processId),
            "processexplorer" => externalTools.OpenWithProcessExplorer(processId),
            "procmon" => externalTools.OpenWithProcmon(processId),
            _ => false
        };

        if (!success)
        {
            _logger?.LogWarning("Impossibile avviare {Tool} per PID {ProcessId}", preferredTool, processId);
        }
    }

    private static void ShowConsoleGitMonitor(ConsoleProcessMonitor monitor)
    {
        Console.Clear();
        Console.WriteLine("=== Monitor Processi Console/Git ===");
        Console.WriteLine("Premi 'r' per refresh, 'q' per tornare al menu principale\n");

        var processes = monitor.GetConsoleAndGitProcesses();

        if (!processes.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Nessun processo console/Git trovato");
            Console.ResetColor();
            Console.WriteLine("\nPremi un tasto per continuare...");
            Console.ReadKey();
            return;
        }

        // Header della tabella
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 180));
        Console.Write($"{"Status",-8} {"PID",-8} {"Nome",-18} {"Parent",-15} {"CPU%",-8} {"MemMB",-10} {"Uptime",-12} {"Rischio",-8} {"Comando",-50}");
        Console.WriteLine();
        Console.WriteLine(new string('=', 180));
        Console.ResetColor();

        // Ordina per rischio e CPU
        var sortedProcesses = processes
            .OrderByDescending(p => p.CommandAnalysis.RiskLevel)
            .ThenByDescending(p => p.CpuUsage)
            .ToList();

        foreach (var proc in sortedProcesses)
        {
            // Colonna Status con icona
            var statusColor = proc.Status switch
            {
                ProcessStatus.Blocked => ConsoleColor.Red,
                ProcessStatus.HighCpu => ConsoleColor.Yellow,
                ProcessStatus.HighMemory => ConsoleColor.Magenta,
                ProcessStatus.Suspicious => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Green
            };
            
            Console.ForegroundColor = statusColor;
            Console.Write($"{proc.StatusIcon,-2} {proc.Status.ToString(),-5}");
            Console.ResetColor();

            // PID
            Console.Write($"{proc.ProcessId,-8}");

            // Nome processo (troncato se troppo lungo)
            var processName = proc.ProcessName.Length > 16 
                ? proc.ProcessName.Substring(0, 13) + "..." 
                : proc.ProcessName;
            Console.Write($"{processName,-18}");

            // Parent process
            var parent = proc.ParentProcessName.Length > 13 
                ? proc.ParentProcessName.Substring(0, 10) + "..." 
                : proc.ParentProcessName;
            Console.Write($"{parent,-15}");

            // CPU con barra grafica
            var cpuBar = GetCpuBar(proc.CpuUsage);
            Console.ForegroundColor = proc.CpuUsage > 10 ? ConsoleColor.Yellow : ConsoleColor.Gray;
            Console.Write($"{proc.CpuUsage,5:F1}% {cpuBar,-10}");
            Console.ResetColor();

            // Memoria
            Console.Write($"{proc.MemoryMB,6:F1} MB ");

            // Uptime formattato
            var uptimeStr = FormatUptime(proc.Uptime);
            Console.Write($"{uptimeStr,-12}");

            // Rischio con icona
            var riskColor = proc.CommandAnalysis.RiskLevel switch
            {
                RiskLevel.Critical => ConsoleColor.Red,
                RiskLevel.High => ConsoleColor.DarkRed,
                RiskLevel.Medium => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };
            Console.ForegroundColor = riskColor;
            Console.Write($"{proc.RiskIcon} {proc.CommandAnalysis.RiskLevel,-5}");
            Console.ResetColor();

            // Comando (troncato)
            var cmd = proc.ShortCommandLine;
            if (string.IsNullOrEmpty(cmd))
                cmd = "(nessun comando)";
            
            // Evidenzia comandi Git
            if (cmd.ToLowerInvariant().Contains("git"))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            Console.Write($" {cmd}");
            Console.ResetColor();

            Console.WriteLine();

            // Mostra warning se presenti
            if (proc.CommandAnalysis.Warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var warning in proc.CommandAnalysis.Warnings.Take(2))
                {
                    Console.WriteLine($"         ⚠️  {warning}");
                }
                Console.ResetColor();
            }
        }

        Console.WriteLine(new string('=', 180));
        
        // Statistiche
        var gitCount = processes.Count(p => p.ProcessName.ToLowerInvariant().Contains("git"));
        var consoleCount = processes.Count(p => !p.ProcessName.ToLowerInvariant().Contains("git"));
        var blockedCount = processes.Count(p => p.Status == ProcessStatus.Blocked);
        var highRiskCount = processes.Count(p => p.CommandAnalysis.RiskLevel >= RiskLevel.High);

        Console.WriteLine($"\nStatistiche: Git={gitCount} | Console={consoleCount} | Bloccati={blockedCount} | Alto Rischio={highRiskCount}");
        
        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
        Console.Clear();
    }

    private static string GetCpuBar(double cpuUsage)
    {
        var bars = (int)(cpuUsage / 2); // Ogni barra = 2% CPU
        bars = Math.Min(bars, 10); // Max 10 barre
        
        var bar = new string('█', bars);
        var empty = new string('░', 10 - bars);
        
        return $"[{bar}{empty}]";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        else if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        else if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        else
            return $"{uptime.Seconds}s";
    }
}
