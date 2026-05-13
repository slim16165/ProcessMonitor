using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

        // Inizializza classificatore processi Windows (PRIMA dei servizi)
        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "windows_system_processes.json");
        ProcessMonitorClassifier.Initialize(jsonPath);

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
        var gitRepositoryAnalyzer = new GitRepositoryAnalyzer(monitorConfig);
        var everythingService = new EverythingService(externalToolsConfig.EverythingPath);
        var processDetailsService = new ProcessDetailsService();
        var consoleSessionManager = new ConsoleSessionManager(monitorConfig);
        var processSnapshotService = new ProcessSnapshotService(monitorConfig, commandAnalyzer);
        var ownerResolver = new OwnerResolver();
        var tagEnricher = new TagEnricher();
        var systemHealthService = new SystemHealthService(processSnapshotService, ownerResolver, tagEnricher);
        var slowdownAnalyzer = new SlowdownAnalyzerService(processSnapshotService, ownerResolver, tagEnricher, systemHealthService);
        var slowdownPlanner = new SlowdownPlannerService();
        var processTreeResolver = new ProcessTreeResolver(processSnapshotService, ownerResolver, tagEnricher);
        var processSnapshotArchive = new ProcessSnapshotArchiveService(processSnapshotService, ownerResolver, tagEnricher, systemHealthService);
        var remediationPlanner = new RemediationPlanner();

        if (await TryRunAgentCommand(args, processTreeResolver, remediationPlanner, processSnapshotArchive, systemHealthService, slowdownAnalyzer, slowdownPlanner, processSnapshotService, ownerResolver, tagEnricher))
        {
            return;
        }

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
        _logger.LogInformation("  'r' - Trova repo Git enormi");
        _logger.LogInformation("  'd' - Dettagli processo Git (richiede PID)");
        _logger.LogInformation("  'z' - Gestione sessioni console zombie");
        _logger.LogInformation("  'g' - Apri Resource Monitor");
        _logger.LogInformation("  'h' - Health/triage live");
        _logger.LogInformation("  'y' - Why slow / root cause");
        _logger.LogInformation("  'f' - Focus/filter live");
        _logger.LogInformation("  'i' - Inspect process tree (richiede PID)");
        _logger.LogInformation("  'j' - Inspect process tree JSON (richiede PID)");
        _logger.LogInformation("  'm' - Remediation dry-run (richiede PID)");
        _logger.LogInformation("  'k' - Remediation apply (richiede PID)");
        _logger.LogInformation("  'n' - Salva snapshot processi");
        _logger.LogInformation("  'v' - Diff snapshot (Enter = latest vs stato attuale)");

        if (Console.IsInputRedirected)
        {
            _logger.LogWarning("Input console rediretto: modalità interattiva non disponibile");
            return;
        }

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
            if (!Console.IsInputRedirected && Console.KeyAvailable)
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
                else if (key.KeyChar == 'r' || key.KeyChar == 'R')
                {
                    await FindLargeGitRepositories(gitRepositoryAnalyzer, everythingService);
                }
                else if (key.KeyChar == 'd' || key.KeyChar == 'D')
                {
                    await ShowGitProcessDetails(processDetailsService, externalTools);
                }
                else if (key.KeyChar == 'z' || key.KeyChar == 'Z')
                {
                    await ManageZombieSessions(consoleSessionManager);
                }
                else if (key.KeyChar == 'g' || key.KeyChar == 'G')
                {
                    OpenResourceMonitor(externalTools);
                }
                else if (key.KeyChar == 'h' || key.KeyChar == 'H')
                {
                    ShowSystemHealth(systemHealthService, asJson: false);
                }
                else if (key.KeyChar == 'y' || key.KeyChar == 'Y')
                {
                    ShowSlowdownDiagnosis(slowdownAnalyzer, slowdownPlanner, asJson: false);
                }
                else if (key.KeyChar == 'f' || key.KeyChar == 'F')
                {
                    ShowFocusView(slowdownAnalyzer);
                }
                else if (key.KeyChar == 'i' || key.KeyChar == 'I')
                {
                    await InspectProcessTree(processTreeResolver, remediationPlanner, asJson: false, applyRemediation: false);
                }
                else if (key.KeyChar == 'j' || key.KeyChar == 'J')
                {
                    await InspectProcessTree(processTreeResolver, remediationPlanner, asJson: true, applyRemediation: false);
                }
                else if (key.KeyChar == 'm' || key.KeyChar == 'M')
                {
                    await InspectProcessTree(processTreeResolver, remediationPlanner, asJson: false, applyRemediation: false, showOnlyRemediation: true);
                }
                else if (key.KeyChar == 'k' || key.KeyChar == 'K')
                {
                    await InspectProcessTree(processTreeResolver, remediationPlanner, asJson: false, applyRemediation: true);
                }
                else if (key.KeyChar == 'n' || key.KeyChar == 'N')
                {
                    SaveProcessSnapshot(processSnapshotArchive);
                }
                else if (key.KeyChar == 'v' || key.KeyChar == 'V')
                {
                    DiffProcessSnapshots(processSnapshotArchive);
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

    private static async Task FindLargeGitRepositories(
        GitRepositoryAnalyzer gitRepositoryAnalyzer,
        EverythingService everythingService)
    {
        Console.WriteLine("\n=== Trova Repository Git Enormi ===");
        Console.WriteLine("Scegli metodo:");
        Console.WriteLine("  1 - Usa Everything (più veloce, se disponibile)");
        Console.WriteLine("  2 - Usa du.exe/PowerShell (più lento ma accurato)");
        Console.Write("Scelta (1 o 2): ");

        var choice = Console.ReadLine();
        var repositories = new List<LargeGitRepository>();

        try
        {
            if (choice == "1" && everythingService.IsAvailable())
            {
                Console.WriteLine("\n🔍 Cerca con Everything...");
                repositories = await everythingService.FindLargeGitDirsWithEverythingAsync(20);
            }
            else
            {
                Console.Write("\nInserisci path root da analizzare (lascia vuoto per C:\\Users): ");
                var rootPath = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                }

                Console.WriteLine($"\n🔍 Analizza repository Git in {rootPath}...");
                Console.WriteLine("(Questo potrebbe richiedere alcuni minuti...)");
                
                repositories = await gitRepositoryAnalyzer.FindLargeGitRepositoriesAsync(rootPath, 20, 100);
            }

            if (!repositories.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n✓ Nessun repository Git grande trovato");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"\n📦 Trovati {repositories.Count} repository Git grandi:\n");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"{"Repository",-60} {"Dimensione .git",-20} {"Dimensione Totale",-20} {"File",-10}");
                Console.WriteLine(new string('-', 120));
                Console.ResetColor();

                foreach (var repo in repositories)
                {
                    var sizeColor = repo.IsLarge ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.ForegroundColor = sizeColor;
                    var repoName = repo.RepositoryPath.Length > 58 
                        ? repo.RepositoryPath.Substring(0, 55) + "..." 
                        : repo.RepositoryPath;
                    Console.Write($"{repoName,-60}");
                    Console.ResetColor();
                    Console.WriteLine($"{repo.FormattedGitSize,-20} {repo.FormattedTotalSize,-20} {repo.TrackedFileCount,-10}");
                }

                Console.WriteLine(new string('-', 120));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n💡 Suggerimento: Considera di escludere questi repository dal caching Git");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ Errore: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static async Task ShowGitProcessDetails(
        ProcessDetailsService processDetailsService,
        ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo Git da analizzare: ");
        if (!int.TryParse(Console.ReadLine(), out var pid))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PID non valido");
            Console.ResetColor();
            return;
        }

        try
        {
            Console.WriteLine("\n=== Dettagli Processo Git ===");
            Console.WriteLine("Analisi in corso...\n");

            var details = await processDetailsService.GetGitProcessDetailsAsync(pid);

            Console.WriteLine($"PID: {details.ProcessId}");
            Console.WriteLine($"Nome: {details.ProcessName}");
            Console.WriteLine($"Start Time: {details.StartTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"CPU: {details.CpuUsage:F2}%");
            Console.WriteLine($"Memoria: {details.MemoryMB:F2} MB");
            Console.WriteLine($"Risponde: {(details.IsResponding ? "Sì" : "No")}");

            if (!string.IsNullOrEmpty(details.CommandLine))
            {
                Console.WriteLine($"\nComando:");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  {details.CommandLine}");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(details.WorkingDirectory))
            {
                Console.WriteLine($"\nWorking Directory:");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {details.WorkingDirectory}");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(details.GitCommand))
            {
                Console.WriteLine($"\nComando Git: {details.GitCommand}");
            }

            if (!string.IsNullOrEmpty(details.GitRepository))
            {
                Console.WriteLine($"\nRepository Git:");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {details.GitRepository}");
                Console.ResetColor();
            }

            Console.WriteLine($"\nI/O Disco:");
            Console.WriteLine($"  Lettura: {details.IoInfo.ReadMBps:F2} MB/s");
            Console.WriteLine($"  Scrittura: {details.IoInfo.WriteMBps:F2} MB/s");
            Console.WriteLine($"  Totale: {details.IoInfo.TotalMBps:F2} MB/s");

            if (details.IsBlockedOnIo)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n⚠️  Processo bloccato su I/O!");
                Console.ResetColor();
            }

            Console.WriteLine("\nAzioni disponibili:");
            Console.WriteLine("  'e' - Apri Process Explorer");
            Console.WriteLine("  'g' - Apri Resource Monitor");
            Console.WriteLine("  'p' - Apri Procmon");
            Console.Write("\nScelta (o Enter per continuare): ");

            var key = Console.ReadKey();
            if (key.KeyChar == 'e' || key.KeyChar == 'E')
            {
                externalTools.OpenWithProcessExplorer(pid);
            }
            else if (key.KeyChar == 'g' || key.KeyChar == 'G')
            {
                externalTools.OpenWithResourceMonitor(pid);
            }
            else if (key.KeyChar == 'p' || key.KeyChar == 'P')
            {
                externalTools.OpenWithProcmon(pid);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ Errore: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static async Task ManageZombieSessions(ConsoleSessionManager consoleSessionManager)
    {
        Console.WriteLine("\n=== Gestione Sessioni Console Zombie ===");

        var zombies = consoleSessionManager.FindZombieSessions();

        if (!zombies.Any())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Nessuna sessione zombie trovata");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"\n🔍 Trovate {zombies.Count} sessioni zombie:\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{"PID",-8} {"Uptime",-15} {"CPU%",-8} {"MemMB",-10} {"Comando",-50}");
            Console.WriteLine(new string('-', 100));
            Console.ResetColor();

            foreach (var zombie in zombies)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{zombie.ProcessId,-8} {zombie.FormattedUptime,-15} {zombie.CpuUsage,-8:F1} {zombie.MemoryMB,-10:F1} ");
                Console.ResetColor();
                
                var cmd = zombie.CommandLine.Length > 48 
                    ? zombie.CommandLine.Substring(0, 45) + "..." 
                    : zombie.CommandLine;
                Console.WriteLine(cmd);
            }

            Console.WriteLine(new string('-', 100));
            Console.WriteLine("\nAzioni disponibili:");
            Console.WriteLine("  'c' - Chiudi sessioni zombie");
            Console.WriteLine("  'o' - Chiudi sessioni più vecchie di 1 ora");
            Console.WriteLine("  't' - Chiudi TGitCache.exe");
            Console.Write("\nScelta (o Enter per continuare): ");

            var key = Console.ReadKey();
            Console.WriteLine();

            if (key.KeyChar == 'c' || key.KeyChar == 'C')
            {
                var closed = consoleSessionManager.CloseOldSessions(TimeSpan.FromMinutes(0), force: false);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✓ Chiuse {closed} sessioni zombie");
                Console.ResetColor();
            }
            else if (key.KeyChar == 'o' || key.KeyChar == 'O')
            {
                var closed = consoleSessionManager.CloseOldSessions(TimeSpan.FromHours(1), force: false);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✓ Chiuse {closed} sessioni vecchie");
                Console.ResetColor();
            }
            else if (key.KeyChar == 't' || key.KeyChar == 'T')
            {
                var closed = consoleSessionManager.CloseTGitCache();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✓ Chiusi {closed} processi TGitCache.exe");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void OpenResourceMonitor(ExternalToolsService externalTools)
    {
        Console.Write("\nInserisci PID del processo (opzionale, lascia vuoto per aprire senza filtro): ");
        var input = Console.ReadLine();
        
        int? pid = null;
        if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out var parsedPid))
        {
            pid = parsedPid;
        }

        var result = externalTools.OpenWithResourceMonitor(pid);
        if (result)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Resource Monitor avviato con successo");
            Console.ResetColor();
            if (pid.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Nota: Filtra manualmente per PID {pid.Value} nella scheda Disk");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Errore nell'avvio di Resource Monitor");
            Console.ResetColor();
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static async Task InspectProcessTree(
        ProcessTreeResolver resolver,
        RemediationPlanner planner,
        bool asJson,
        bool applyRemediation,
        bool showOnlyRemediation = false)
    {
        Console.Write("\nInserisci PID del processo da investigare: ");
        if (!int.TryParse(Console.ReadLine(), out var pid))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("PID non valido");
            Console.ResetColor();
            return;
        }

        var investigation = resolver.InvestigateByPid(pid);
        investigation.RemediationPlan = planner.BuildPlan(investigation);

        if (applyRemediation)
        {
            ApplyRemediationPlan(investigation.RemediationPlan);
        }

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(investigation, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (showOnlyRemediation)
        {
            PrintRemediationPlan(investigation.RemediationPlan);
        }
        else
        {
            PrintInvestigation(investigation);
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
        await Task.CompletedTask;
    }

    private static async Task<bool> TryRunAgentCommand(
        string[] args,
        ProcessTreeResolver resolver,
        RemediationPlanner planner,
        ProcessSnapshotArchiveService snapshotArchive,
        SystemHealthService healthService,
        SlowdownAnalyzerService slowdownAnalyzer,
        SlowdownPlannerService slowdownPlanner,
        ProcessSnapshotService processSnapshotService,
        OwnerResolver ownerResolver,
        TagEnricher tagEnricher)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            if (command == "snapshot-save")
            {
                var note = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : "manual";
                var snapshot = snapshotArchive.SaveSnapshot(note);
                Console.WriteLine($"Snapshot salvato: {snapshot.SnapshotId} ({snapshot.Processes.Count} processi)");
                return true;
            }

            if (command == "snapshot-list")
            {
                foreach (var snapshotId in snapshotArchive.ListSnapshots())
                {
                    Console.WriteLine(snapshotId);
                }
                return true;
            }

            if (command == "snapshot-diff" && args.Length >= 3)
            {
                var diff = snapshotArchive.DiffSnapshots(args[1], args[2]);
                PrintSnapshotDiff(diff);
                return true;
            }

            if (command == "snapshot-diff-latest")
            {
                var note = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : "current-live";
                var diff = snapshotArchive.DiffLatestSnapshotAgainstCurrent(note);
                PrintSnapshotDiff(diff);
                return true;
            }

            if (command == "snapshot-diff-current" && args.Length >= 2)
            {
                var diff = snapshotArchive.DiffSnapshotAgainstCurrent(args[1]);
                PrintSnapshotDiff(diff);
                return true;
            }

            if (command == "snapshot-diff-health" && args.Length >= 3)
            {
                var delta = snapshotArchive.DiffSnapshotHealth(args[1], args[2]);
                PrintHealthDelta(delta);
                return true;
            }

            if (command == "health")
            {
                PrintSystemHealth(healthService.CaptureHealthSnapshot());
                return true;
            }

            if (command == "health-json")
            {
                Console.WriteLine(JsonSerializer.Serialize(healthService.CaptureHealthSnapshot(), new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }

            if (command == "export-json")
            {
                try
                {
                    var snapshot = healthService.CaptureHealthSnapshot();
                    var outputPath = args.Length >= 2 ? args[1] : $"system_health_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    ReportExporter.ExportToJson(snapshot, outputPath);
                    Console.WriteLine($"Exported system health to {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error exporting to JSON: {ex.Message}");
                }
                return true;
            }

            if (command == "export-csv")
            {
                try
                {
                    var snapshot = healthService.CaptureHealthSnapshot();
                    var outputPath = args.Length >= 2 ? args[1] : $"system_health_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                    ReportExporter.ExportToCsv(snapshot.CpuTop, outputPath);
                    Console.WriteLine($"Exported top CPU processes to {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error exporting to CSV: {ex.Message}");
                }
                return true;
            }

            if (command == "threshold-baseline")
            {
                var baseline = DynamicThresholdService.CalculateBaseline();
                Console.WriteLine($"Baseline based on {baseline.SampleCount} snapshots:");
                Console.WriteLine($"  CPU: {baseline.CpuMean:F1}% ± {baseline.CpuStdDev:F1}% (min: {baseline.CpuMin:F1}%, max: {baseline.CpuMax:F1}%)");
                Console.WriteLine($"  Disk: {baseline.DiskMean:F1}% ± {baseline.DiskStdDev:F1}% (min: {baseline.DiskMin:F1}%, max: {baseline.DiskMax:F1}%)");
                Console.WriteLine($"  Memory: {baseline.MemoryMean:F1}MB ± {baseline.MemoryStdDev:F1}MB (min: {baseline.MemoryMin:F1}MB, max: {baseline.MemoryMax:F1}MB)");
                return true;
            }

            if (command == "threshold-check")
            {
                var snapshot = healthService.CaptureHealthSnapshot();
                DynamicThresholdService.AddSnapshot(snapshot);
                var anomalies = DynamicThresholdService.DetectAnomalies(snapshot);
                
                if (anomalies.Count == 0)
                {
                    Console.WriteLine("No anomalies detected.");
                }
                else
                {
                    Console.WriteLine($"Detected {anomalies.Count} anomalies:");
                    foreach (var anomaly in anomalies)
                    {
                        Console.WriteLine($"  [{anomaly.Type.ToUpper()}] {anomaly.Severity.ToUpper()}: Current={anomaly.CurrentValue:F1}, Baseline={anomaly.BaselineMean:F1}±{anomaly.BaselineStdDev:F1}, Deviation={anomaly.Deviation:F1}σ");
                    }
                }
                return true;
            }

            if (command == "threshold-add")
            {
                var snapshot = healthService.CaptureHealthSnapshot();
                DynamicThresholdService.AddSnapshot(snapshot);
                Console.WriteLine($"Added snapshot to history. Total snapshots: {DynamicThresholdService.CalculateBaseline().SampleCount}");
                return true;
            }

            if (command == "git-map")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: git-map <processId> <repositoryPath> [branch]");
                    return true;
                }
                
                if (!int.TryParse(args[1], out var processId))
                {
                    Console.WriteLine("Invalid process ID");
                    return true;
                }

                var repoPath = args[2];
                var branch = args.Length >= 4 ? args[3] : "main";
                
                GitRepositoryMapper.AddMapping(processId, repoPath, branch);
                Console.WriteLine($"Mapped process {processId} to repository {repoPath} (branch: {branch})");
                return true;
            }

            if (command == "git-show")
            {
                if (args.Length >= 2 && int.TryParse(args[1], out var processId))
                {
                    var repos = GitRepositoryMapper.GetRepositoriesForProcess(processId);
                    Console.WriteLine($"Repositories for process {processId}:");
                    if (repos.Count == 0)
                    {
                        Console.WriteLine("  None");
                    }
                    else
                    {
                        foreach (var repo in repos)
                        {
                            Console.WriteLine($"  - {repo.Path} (branch: {repo.Branch}, last seen: {repo.LastSeen:yyyy-MM-dd HH:mm})");
                        }
                    }
                }
                else
                {
                    var allMappings = GitRepositoryMapper.GetAllMappings();
                    Console.WriteLine($"All Git mappings ({allMappings.Count} processes):");
                    foreach (var kvp in allMappings)
                    {
                        Console.WriteLine($"  Process {kvp.Key}:");
                        foreach (var repo in kvp.Value)
                        {
                            Console.WriteLine($"    - {repo.Path} (branch: {repo.Branch})");
                        }
                    }
                }
                return true;
            }

            if (command == "git-clear")
            {
                GitRepositoryMapper.ClearMappings();
                Console.WriteLine("Cleared all Git repository mappings");
                return true;
            }

            if (command == "git-stuck")
            {
                var snapshot = processSnapshotService.CaptureSnapshot();
                ownerResolver.AssignOwners(snapshot);
                tagEnricher.ApplyTags(snapshot);
                
                var stuckProcesses = GitRepositoryMapper.GetStuckGitProcesses(snapshot);
                
                if (stuckProcesses.Count == 0)
                {
                    Console.WriteLine("No stuck Git processes detected.");
                }
                else
                {
                    Console.WriteLine($"Found {stuckProcesses.Count} Git processes:");
                    foreach (var proc in stuckProcesses)
                    {
                        Console.WriteLine($"  [{proc.Status}] PID={proc.ProcessId} CPU={proc.CpuUsage:F1}% Duration={proc.Duration.TotalMinutes:F1}min");
                        Console.WriteLine($"    Command: {proc.CommandLine}");
                        if (proc.Repositories.Count > 0)
                        {
                            Console.WriteLine($"    Repositories:");
                            foreach (var repo in proc.Repositories)
                            {
                                Console.WriteLine($"      - {repo.Path} (branch: {repo.Branch})");
                            }
                        }
                    }
                }
                return true;
            }

            if (command == "why-slow")
            {
                var focus = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : null;
                var diagnosis = slowdownAnalyzer.Diagnose(focus);
                PrintSlowdownDiagnosis(diagnosis, slowdownPlanner.BuildPlan(diagnosis));
                return true;
            }

            if (command == "why-slow-json")
            {
                var focus = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : null;
                Console.WriteLine(JsonSerializer.Serialize(slowdownAnalyzer.Diagnose(focus), new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }

            if (command == "plan-slowdown")
            {
                var focus = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : null;
                var diagnosis = slowdownAnalyzer.Diagnose(focus);
                PrintSlowdownPlan(slowdownPlanner.BuildPlan(diagnosis));
                return true;
            }

            if (command == "plan-json")
            {
                var focus = args.Length >= 2 ? string.Join(' ', args.Skip(1)) : null;
                var diagnosis = slowdownAnalyzer.Diagnose(focus);
                Console.WriteLine(JsonSerializer.Serialize(slowdownPlanner.BuildPlan(diagnosis), new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }

            if (command == "focus" && args.Length >= 2)
            {
                var focus = string.Join(' ', args.Skip(1));
                var diagnosis = slowdownAnalyzer.Diagnose(focus);
                PrintFocusProcesses(focus, diagnosis.FocusProcesses);
                return true;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out var pid))
            {
                return false;
            }

            var investigation = resolver.InvestigateByPid(pid);
            investigation.RemediationPlan = planner.BuildPlan(investigation);

            switch (command)
            {
                case "inspect":
                    PrintInvestigation(investigation);
                    return true;
                case "inspect-json":
                    Console.WriteLine(JsonSerializer.Serialize(investigation, new JsonSerializerOptions { WriteIndented = true }));
                    return true;
                case "remediate-dry-run":
                    PrintRemediationPlan(investigation.RemediationPlan);
                    return true;
                case "remediate-apply":
                    PrintRemediationPlan(investigation.RemediationPlan);
                    ApplyRemediationPlan(investigation.RemediationPlan);
                    return true;
                case "plan-tree":
                    PrintRemediationPlan(investigation.RemediationPlan);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore comando '{command}': {ex.Message}");
            Environment.ExitCode = 1;
            return true;
        }
    }

    private static void PrintInvestigation(ProcessInvestigation investigation)
    {
        Console.WriteLine("\n=== Process Investigation ===");
        Console.WriteLine($"Root: {investigation.RootProcessName} ({investigation.RootProcessId})");
        Console.WriteLine($"Captured: {investigation.CapturedAt:yyyy-MM-dd HH:mm:ss}");

        if (investigation.Root == null)
        {
            Console.WriteLine("Nessun albero disponibile.");
            return;
        }

        PrintTreeNode(investigation.Root, 0);

        if (investigation.Evidence.Any())
        {
            Console.WriteLine("\nEvidence:");
            foreach (var evidence in investigation.Evidence.Take(20))
            {
                Console.WriteLine($"  PID {evidence.ProcessId}: {evidence.Kind} - {evidence.Detail}");
            }
        }

        if (investigation.Orphans.Any())
        {
            Console.WriteLine("\nOrphans:");
            foreach (var orphan in investigation.Orphans.Take(10))
            {
                Console.WriteLine($"  PID {orphan.ProcessId} {orphan.ProcessName} {orphan.CommandLine}");
            }
        }

        if (investigation.Owners.Any())
        {
            Console.WriteLine("\nOwners:");
            foreach (var owner in investigation.Owners.Take(10))
            {
                Console.WriteLine($"  {string.Join(" > ", owner.OwnerPath)}: {owner.ProcessCount} processi [{string.Join(", ", owner.Tags.Take(6))}]");
            }
        }

        Console.WriteLine();
        PrintRemediationPlan(investigation.RemediationPlan);
    }

    private static void PrintTreeNode(ProcessTreeNode node, int depth)
    {
        var indent = new string(' ', depth * 2);
        Console.WriteLine($"{indent}- PID {node.ProcessId} {node.ProcessName} [{node.LaunchCategory}] CPU={node.CpuUsage:F1}% MEM={node.MemoryMB:F1}MB TCP={(node.HasTcpActivity ? "Y" : "N")}");
        if (node.OwnerPath.Any())
        {
            Console.WriteLine($"{indent}  OWNER: {string.Join(" > ", node.OwnerPath)}");
        }
        if (node.Tags.Any())
        {
            Console.WriteLine($"{indent}  TAGS: {string.Join(", ", node.Tags)}");
        }

        if (!string.IsNullOrWhiteSpace(node.CommandLine))
        {
            Console.WriteLine($"{indent}  CMD: {node.CommandLine}");
        }

        if (!string.IsNullOrWhiteSpace(node.WorkingDirectory))
        {
            Console.WriteLine($"{indent}  CWD: {node.WorkingDirectory}");
        }

        foreach (var child in node.Children)
        {
            PrintTreeNode(child, depth + 1);
        }
    }

    private static void PrintRemediationPlan(RemediationPlan plan)
    {
        Console.WriteLine("=== Remediation Plan ===");
        Console.WriteLine(plan.Summary);
        Console.WriteLine($"Severity: {plan.Severity} | Confidence: {plan.Confidence}");

        foreach (var reason in plan.Reasons)
        {
            Console.WriteLine($"  - {reason}");
        }

        if (plan.KillOrder.Any())
        {
            Console.WriteLine($"Kill order: {string.Join(", ", plan.KillOrder)}");
        }

        if (plan.HoldOpen.Any())
        {
            Console.WriteLine($"Hold open: {string.Join(", ", plan.HoldOpen)}");
        }

        if (plan.SuggestedActions.Any())
        {
            Console.WriteLine("Suggested actions:");
            foreach (var action in plan.SuggestedActions.Take(10))
            {
                Console.WriteLine($"  [{action.Type}] {action.Summary} ({action.Severity}/{action.Confidence})");
            }
        }
    }

    private static void ApplyRemediationPlan(RemediationPlan plan)
    {
        if (!plan.KillOrder.Any())
        {
            Console.WriteLine("Nessun processo da terminare.");
            return;
        }

        Console.WriteLine("\n=== Apply Remediation ===");
        foreach (var pid in plan.KillOrder)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill(entireProcessTree: false);
                Console.WriteLine($"Terminato PID {pid}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore terminando PID {pid}: {ex.Message}");
            }
        }
    }

    private static void SaveProcessSnapshot(ProcessSnapshotArchiveService snapshotArchive)
    {
        Console.Write("\nNota snapshot: ");
        var note = Console.ReadLine();
        var snapshot = snapshotArchive.SaveSnapshot(string.IsNullOrWhiteSpace(note) ? "manual" : note);
        Console.WriteLine($"Snapshot salvato: {snapshot.SnapshotId} ({snapshot.Processes.Count} processi)");
        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void DiffProcessSnapshots(ProcessSnapshotArchiveService snapshotArchive)
    {
        var snapshots = snapshotArchive.ListSnapshots();
        if (snapshots.Count < 1)
        {
            Console.WriteLine("\nServe almeno uno snapshot salvato.");
            Console.WriteLine("\nPremi un tasto per continuare...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("\nSnapshot disponibili:");
        foreach (var snapshotId in snapshots.Take(10))
        {
            Console.WriteLine($"  {snapshotId}");
        }

        Console.Write("Baseline snapshot id (Enter = latest): ");
        var baselineId = Console.ReadLine();
        Console.Write("Current snapshot id (Enter = stato attuale live): ");
        var currentId = Console.ReadLine();

        try
        {
            var resolvedBaselineId = string.IsNullOrWhiteSpace(baselineId)
                ? snapshotArchive.GetLatestSnapshotId()
                : baselineId;

            if (string.IsNullOrWhiteSpace(resolvedBaselineId))
            {
                Console.WriteLine("Snapshot baseline non valido.");
                Console.WriteLine("\nPremi un tasto per continuare...");
                Console.ReadKey();
                return;
            }

            var diff = string.IsNullOrWhiteSpace(currentId)
                ? snapshotArchive.DiffSnapshotAgainstCurrent(resolvedBaselineId)
                : snapshotArchive.DiffSnapshots(resolvedBaselineId, currentId);
            PrintSnapshotDiff(diff);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore diff snapshot: {ex.Message}");
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void PrintSnapshotDiff(ProcessSnapshotDiff diff)
    {
        Console.WriteLine("=== Snapshot Diff ===");
        Console.WriteLine($"Baseline: {diff.BaselineId} ({diff.BaselineCount})");
        Console.WriteLine($"Current:  {diff.CurrentId} ({diff.CurrentCount})");
        Console.WriteLine($"Delta:    {diff.DeltaCount}");

        if (diff.HealthDelta != null)
        {
            Console.WriteLine($"\nHealth: {diff.HealthDelta.BaselineBottleneck} -> {diff.HealthDelta.CurrentBottleneck}");
            Console.WriteLine($"  CPU delta: {diff.HealthDelta.CpuDelta:+#;-#;0}%");
            Console.WriteLine($"  Disk delta: {diff.HealthDelta.DiskDelta:+#;-#;0}%");
            Console.WriteLine($"  Free memory delta: {diff.HealthDelta.MemoryAvailableDeltaMB:+#;-#;0} MB");
            Console.WriteLine($"  Page reads delta: {diff.HealthDelta.PageReadsDelta:+#;-#;0}/s");
        }

        if (diff.OwnerDeltas.Any())
        {
            Console.WriteLine("\nOwner deltas:");
            foreach (var ownerDelta in diff.OwnerDeltas.Take(15))
            {
                Console.WriteLine($"  {string.Join(" > ", ownerDelta.OwnerPath)}: {ownerDelta.BaselineCount} -> {ownerDelta.CurrentCount} ({ownerDelta.DeltaCount:+#;-#;0})");
            }
        }

        if (diff.TagDeltas.Any())
        {
            Console.WriteLine("\nTag deltas:");
            foreach (var tagDelta in diff.TagDeltas.Take(12))
            {
                Console.WriteLine($"  {tagDelta.Tag}: {tagDelta.BaselineCount} -> {tagDelta.CurrentCount} ({tagDelta.DeltaCount:+#;-#;0})");
            }
        }

        if (diff.NewSignatures.Any())
        {
            Console.WriteLine("\nNew signatures:");
            foreach (var item in diff.NewSignatures.Take(15))
            {
                Console.WriteLine($"  {item.OwnerId} | {item.ProcessName}: +{item.DeltaCount} [{item.SignatureDetail}]");
            }
        }

        if (diff.RemovedSignatures.Any())
        {
            Console.WriteLine("\nRemoved signatures:");
            foreach (var item in diff.RemovedSignatures.Take(15))
            {
                Console.WriteLine($"  {item.OwnerId} | {item.ProcessName}: {item.DeltaCount} [{item.SignatureDetail}]");
            }
        }
    }

    private static void ShowSystemHealth(SystemHealthService healthService, bool asJson)
    {
        var health = healthService.CaptureHealthSnapshot();
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintSystemHealth(health);
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void PrintSystemHealth(SystemHealthSnapshot health)
    {
        Console.WriteLine("=== System Health ===");
        Console.WriteLine($"Captured: {health.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Uptime:   {health.MachineUptime:dd\\.hh\\:mm\\:ss}");
        Console.WriteLine($"Pressure: {health.Pressure.PrimaryBottleneck}");
        if (!string.IsNullOrWhiteSpace(health.Pressure.SecondaryBottleneck))
        {
            Console.WriteLine($"Secondary:{health.Pressure.SecondaryBottleneck}");
        }
        Console.WriteLine($"Summary:  {health.Pressure.Summary}");
        Console.WriteLine();
        Console.WriteLine($"CPU     total={health.TotalCpuPercent:F0}% user={health.UserCpuPercent:F0}% kernel={health.KernelCpuPercent:F0}% intr={health.InterruptCpuPercent:F0}% dpc={health.DpcCpuPercent:F0}%");
        Console.WriteLine($"Disk    busy={health.DiskBusyPercent:F0}% queue={health.DiskQueueLength:F1} read={(health.DiskReadBytesPerSec / (1024 * 1024)):F1}MB/s write={(health.DiskWriteBytesPerSec / (1024 * 1024)):F1}MB/s");
        Console.WriteLine($"Memory  avail={health.AvailableMemoryMB:F0}MB commit={health.CommittedMemoryMB:F0}/{health.CommitLimitMB:F0}MB pages={health.PagesPerSec:F0}/s pageReads={health.PageReadsPerSec:F0}/s pagefile={health.PageFileUsageMB:F0}/{health.PageFileAllocatedMB:F0}MB");

        PrintTopProcesses("Top CPU", health.CpuTop, sample => $"{sample.CpuPercent,5:F1}%");
        PrintTopProcesses("Top IO", health.IoTop, sample => $"{sample.ReadMBps + sample.WriteMBps,5:F2} MB/s");
        PrintTopProcesses("Top MEM", health.MemoryTop, sample => $"{sample.MemoryMB,6:F0} MB");

        if (health.Suspects.Any())
        {
            Console.WriteLine("\nSuspects:");
            foreach (var suspect in health.Suspects.Take(10))
            {
                Console.WriteLine($"  {string.Join(" > ", suspect.OwnerPath),-28} cpu={suspect.CpuPercent,5:F1}% io={suspect.ReadMBps + suspect.WriteMBps,6:F2}MB/s mem={suspect.MemoryMB,7:F0}MB [{suspect.DominantReason}]");
            }
        }
    }

    private static void PrintTopProcesses(string title, IEnumerable<TopProcessSample> samples, Func<TopProcessSample, string> metricSelector)
    {
        Console.WriteLine($"\n{title}:");
        foreach (var sample in samples.Take(8))
        {
            Console.WriteLine($"  {sample.ProcessName,-24} PID={sample.ProcessId,-7} {metricSelector(sample),12} owner={string.Join(" > ", sample.OwnerPath),-24} reason={sample.Reason}");
        }
    }

    private static void ShowSlowdownDiagnosis(SlowdownAnalyzerService analyzer, SlowdownPlannerService planner, bool asJson)
    {
        var diagnosis = analyzer.Diagnose();
        var slowdownPlan = planner.BuildPlan(diagnosis);

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(diagnosis, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            PrintSlowdownDiagnosis(diagnosis, slowdownPlan);
        }

        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void PrintSlowdownDiagnosis(SlowdownDiagnosis diagnosis, SlowdownPlan slowdownPlan)
    {
        Console.WriteLine("=== Why Slow ===");
        Console.WriteLine(diagnosis.Summary);
        Console.WriteLine();
        PrintSystemHealth(diagnosis.Health);

        if (diagnosis.Reasons.Any())
        {
            Console.WriteLine("\nReason codes:");
            foreach (var reason in diagnosis.Reasons.Take(8))
            {
                Console.WriteLine($"  {reason.Code} ({reason.Severity}/{reason.Confidence})");
                Console.WriteLine($"    {reason.Summary}");
                foreach (var evidence in reason.Evidence.Take(3))
                {
                    Console.WriteLine($"    - {evidence}");
                }
                if (!string.IsNullOrWhiteSpace(reason.SuggestedNextAction))
                {
                    Console.WriteLine($"    next: {reason.SuggestedNextAction}");
                }
            }
        }

        Console.WriteLine();
        PrintSlowdownPlan(slowdownPlan);
    }

    private static void PrintSlowdownPlan(SlowdownPlan plan)
    {
        Console.WriteLine("=== Slowdown Plan ===");
        Console.WriteLine(plan.Summary);
        Console.WriteLine($"Severity: {plan.Severity} | Confidence: {plan.Confidence}");
        foreach (var reason in plan.Reasons.Take(10))
        {
            Console.WriteLine($"  - {reason}");
        }

        if (plan.Actions.Any())
        {
            Console.WriteLine("Actions:");
            foreach (var action in plan.Actions.Take(10))
            {
                Console.WriteLine($"  [{action.Type}] {action.Summary} ({action.Severity}/{action.Confidence})");
                if (!string.IsNullOrWhiteSpace(action.Detail))
                {
                    Console.WriteLine($"    {action.Detail}");
                }
            }
        }
    }

    private static void ShowFocusView(SlowdownAnalyzerService analyzer)
    {
        Console.Write("\nFiltro focus (owner:Windsurf, tag:git, pressure:disk, kind:Security, top cpu): ");
        var focus = Console.ReadLine();
        var diagnosis = analyzer.Diagnose(focus);
        PrintFocusProcesses(string.IsNullOrWhiteSpace(focus) ? "top cpu" : focus, diagnosis.FocusProcesses);
        Console.WriteLine("\nPremi un tasto per continuare...");
        Console.ReadKey();
    }

    private static void PrintFocusProcesses(string focus, IEnumerable<TopProcessSample> samples)
    {
        Console.WriteLine($"=== Focus: {focus} ===");
        foreach (var sample in samples.Take(20))
        {
            Console.WriteLine($"  PID={sample.ProcessId,-7} {sample.ProcessName,-24} cpu={sample.CpuPercent,5:F1}% io={sample.ReadMBps + sample.WriteMBps,6:F2}MB/s mem={sample.MemoryMB,7:F0}MB owner={string.Join(" > ", sample.OwnerPath)} reason={sample.Reason}");
        }
    }

    private static void PrintHealthDelta(HealthSnapshotDelta delta)
    {
        Console.WriteLine("=== Snapshot Health Delta ===");
        Console.WriteLine($"Bottleneck: {delta.BaselineBottleneck} -> {delta.CurrentBottleneck}");
        Console.WriteLine($"CPU delta: {delta.CpuDelta:+#;-#;0}%");
        Console.WriteLine($"Disk delta: {delta.DiskDelta:+#;-#;0}%");
        Console.WriteLine($"Free memory delta: {delta.MemoryAvailableDeltaMB:+#;-#;0} MB");
        Console.WriteLine($"Page reads delta: {delta.PageReadsDelta:+#;-#;0}/s");
    }
}
