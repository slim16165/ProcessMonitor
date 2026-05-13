# Process Monitor - Specifiche Programma C#

## Obiettivo

Creare un programma C# per monitorare e individuare processi bloccati, operazioni lente e problemi di performance del sistema.

## Funzionalità Principali

### 1. Monitoraggio Processi in Tempo Reale

#### 1.1 Rilevamento Processi Bloccati
- Monitorare processi che non rispondono (`Responding = false`)
- Rilevare processi con CPU alto (>100) per periodo prolungato
- Identificare processi con memoria in crescita costante (possibile memory leak)
- Tracciare processi che consumano I/O eccessivo

#### 1.2 Processi Sospetti
- Processi PowerShell/Python/Node che:
  - Sono attivi da più di X minuti senza output
  - Hanno CPU = 0 ma sono ancora in esecuzione
  - Stanno leggendo/scrivendo continuamente su disco
  - Hanno molti thread ma non fanno progressi

### 2. Analisi Comandi Eseguiti

#### 2.1 Estrazione Command Line
- Per ogni processo sospetto, estrarre la command line completa
- Identificare se contiene operazioni ricorsive pericolose:
  - `Get-ChildItem -Recurse` senza limitazioni
  - Ricerche su directory molto grandi (es: Dropbox, node_modules)
  - Operazioni senza timeout

#### 2.2 Analisi Pattern
- Rilevare pattern comuni di operazioni lente:
  - Ricerca ricorsiva su `C:\` o directory root
  - Operazioni senza `-Depth` limitato
  - Operazioni senza `-Exclude` per directory grandi
  - Loop infiniti o operazioni senza timeout

### 3. Analisi Directory e File System

#### 3.1 Analisi Workspace
- Analizzare la directory corrente per:
  - Numero totale di file
  - Dimensione totale
  - Profondità massima delle directory
  - Presenza di directory grandi (node_modules, vendor, .git)

#### 3.2 Rilevamento Directory Problematiche
- Identificare directory che potrebbero causare lentezza:
  - Directory con > 10.000 file
  - Directory con dimensione > 1GB
  - Directory con profondità > 10 livelli
  - Directory sincronizzate (Dropbox, OneDrive)

### 4. Monitoraggio Performance

#### 4.1 Metriche CPU
- CPU usage per processo
- CPU totale del sistema
- Processi con CPU > 10% per più di 1 minuto
- Processi con CPU = 0 ma ancora attivi (possibile blocco)

#### 4.2 Metriche Memoria
- Working set per processo
- Memoria totale utilizzata
- Processi con memoria in crescita costante
- Possibili memory leak (> 500MB e in crescita)

#### 4.3 Metriche I/O
- Bytes letti/scritti per secondo
- Processi con I/O > 10MB/s
- Processi che leggono/scrivono continuamente
- Operazioni su file system lente

### 5. Alert e Notifiche

#### 5.1 Alert in Tempo Reale
- Notifiche quando viene rilevato un processo bloccato
- Alert per operazioni che impiegano troppo tempo
- Warning per pattern pericolosi nei comandi

#### 5.2 Report
- Report giornaliero/settimanale
- Statistiche su processi più problematici
- Suggerimenti per ottimizzazione

## Architettura Tecnica

### Librerie .NET Necessarie

```xml
<PackageReference Include="System.Diagnostics.PerformanceCounter" Version="7.0.0" />
<PackageReference Include="System.Management" Version="7.0.0" />
<PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />
```

### Classi Principali

#### 1. ProcessMonitor
```csharp
public class ProcessMonitor
{
    // Monitora tutti i processi
    public Task<List<ProcessInfo>> MonitorProcessesAsync(CancellationToken cancellationToken);
    
    // Rileva processi bloccati
    public List<ProcessInfo> DetectBlockedProcesses();
    
    // Analizza comando eseguito
    public CommandAnalysis AnalyzeCommand(int processId);
    
    // Ottieni metriche performance
    public ProcessMetrics GetProcessMetrics(int processId);
}
```

#### 2. ProcessInfo
```csharp
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; }
    public string CommandLine { get; set; }
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; }
    public bool IsResponding { get; set; }
    public DateTime StartTime { get; set; }
    public long IoReadBytes { get; set; }
    public long IoWriteBytes { get; set; }
    public int ThreadCount { get; set; }
    public ProcessStatus Status { get; set; }
}

public enum ProcessStatus
{
    Normal,
    HighCpu,
    Blocked,
    HighMemory,
    HighIo,
    Suspicious
}
```

#### 3. CommandAnalysis
```csharp
public class CommandAnalysis
{
    public string CommandLine { get; set; }
    public bool HasRecursiveSearch { get; set; }
    public bool HasUnlimitedPath { get; set; }
    public bool HasNoDepthLimit { get; set; }
    public bool HasNoExclusions { get; set; }
    public List<string> Warnings { get; set; }
    public RiskLevel RiskLevel { get; set; }
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}
```

#### 4. DirectoryAnalyzer
```csharp
public class DirectoryAnalyzer
{
    // Analizza directory con timeout
    public Task<DirectoryAnalysis> AnalyzeDirectoryAsync(
        string path, 
        int maxDepth = 10, 
        TimeSpan? timeout = null);
    
    // Trova directory problematiche
    public List<ProblematicDirectory> FindProblematicDirectories(string rootPath);
}

public class DirectoryAnalysis
{
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public int MaxDepth { get; set; }
    public List<string> LargeDirectories { get; set; }
    public bool AnalysisCompleted { get; set; }
    public string TimeoutReason { get; set; }
}
```

#### 5. PerformanceCollector
```csharp
public class PerformanceCollector
{
    // Raccoglie metriche CPU
    public Task<CpuMetrics> CollectCpuMetricsAsync();
    
    // Raccoglie metriche memoria
    public Task<MemoryMetrics> CollectMemoryMetricsAsync();
    
    // Raccoglie metriche I/O
    public Task<IoMetrics> CollectIoMetricsAsync(int processId);
}
```

### Interfaccia Utente

#### Opzione 1: Console Application
- Output colorato per diversi livelli di alert
- Menu interattivo per navigare tra funzionalità
- Refresh automatico ogni X secondi

#### Opzione 2: WPF Application
- Dashboard con grafici in tempo reale
- Lista processi con filtri e ordinamento
- Dettagli processo con analisi comando
- Notifiche toast per alert critici

#### Opzione 3: Web API + Frontend
- API REST per monitoraggio
- Dashboard web con visualizzazioni
- Possibilità di integrazione con altri strumenti

## Algoritmi di Rilevamento

### Rilevamento Processo Bloccato

```csharp
public bool IsProcessBlocked(ProcessInfo process)
{
    // Processo non risponde
    if (!process.IsResponding)
        return true;
    
    // CPU = 0 ma processo ancora attivo da > 5 minuti
    if (process.CpuUsage == 0 && 
        DateTime.Now - process.StartTime > TimeSpan.FromMinutes(5))
        return true;
    
    // Memoria in crescita costante (> 100MB/minuto)
    if (process.MemoryGrowthRate > 100 * 1024 * 1024 / 60)
        return true;
    
    return false;
}
```

### Analisi Pattern Comando Pericoloso

```csharp
public CommandAnalysis AnalyzeCommand(string commandLine)
{
    var analysis = new CommandAnalysis { CommandLine = commandLine };
    
    // Pattern pericolosi
    if (commandLine.Contains("Get-ChildItem") && 
        commandLine.Contains("-Recurse"))
    {
        analysis.HasRecursiveSearch = true;
        
        // Verifica se ha limitazioni
        if (!commandLine.Contains("-Depth"))
            analysis.HasNoDepthLimit = true;
        
        if (!commandLine.Contains("-Exclude") && 
            !commandLine.Contains("-Include"))
            analysis.HasNoExclusions = true;
        
        // Verifica path pericoloso
        if (commandLine.Contains("C:\\") || 
            commandLine.Contains("D:\\") ||
            commandLine.Contains("Path C:"))
            analysis.HasUnlimitedPath = true;
    }
    
    // Calcola rischio
    analysis.RiskLevel = CalculateRiskLevel(analysis);
    
    return analysis;
}
```

### Analisi Directory con Timeout

```csharp
public async Task<DirectoryAnalysis> AnalyzeDirectoryAsync(
    string path, 
    int maxDepth = 10, 
    TimeSpan? timeout = null)
{
    var analysis = new DirectoryAnalysis();
    var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
    
    try
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(path, "*", 
                new EnumerationOptions 
                { 
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = maxDepth
                });
            
            foreach (var file in files)
            {
                cts.Token.ThrowIfCancellationRequested();
                
                var info = new FileInfo(file);
                analysis.FileCount++;
                analysis.TotalSize += info.Length;
            }
        }, cts.Token);
        
        analysis.AnalysisCompleted = true;
    }
    catch (OperationCanceledException)
    {
        analysis.TimeoutReason = "Analisi interrotta dopo timeout";
    }
    
    return analysis;
}
```

## Configurazione

### File di Configurazione (appsettings.json)

```json
{
  "ProcessMonitor": {
    "CheckIntervalSeconds": 5,
    "CpuThreshold": 10.0,
    "MemoryThresholdMB": 500,
    "IoThresholdMBps": 10,
    "BlockedProcessTimeoutMinutes": 5,
    "SuspiciousProcesses": ["python", "pwsh", "powershell", "node"],
    "ExcludedDirectories": ["node_modules", "vendor", ".git", ".vs"],
    "MaxDirectoryDepth": 10,
    "DirectoryAnalysisTimeoutSeconds": 5
  },
  "Notifications": {
    "Enabled": true,
    "ShowToast": true,
    "LogToFile": true,
    "LogPath": "logs/process-monitor.log"
  }
}
```

## Esempio di Utilizzo

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        var monitor = new ProcessMonitor();
        var analyzer = new DirectoryAnalyzer();
        
        // Monitoraggio continuo
        var cancellationToken = new CancellationTokenSource();
        
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.Token.IsCancellationRequested)
            {
                var processes = await monitor.MonitorProcessesAsync(
                    cancellationToken.Token);
                
                var blocked = monitor.DetectBlockedProcesses();
                
                foreach (var proc in blocked)
                {
                    var analysis = monitor.AnalyzeCommand(proc.ProcessId);
                    
                    if (analysis.RiskLevel >= RiskLevel.High)
                    {
                        Console.WriteLine(
                            $"⚠️  Processo bloccato: {proc.ProcessName} (PID: {proc.ProcessId})");
                        Console.WriteLine(
                            $"   Comando: {proc.CommandLine}");
                        Console.WriteLine(
                            $"   Rischi: {string.Join(", ", analysis.Warnings)}");
                    }
                }
                
                await Task.Delay(5000, cancellationToken.Token);
            }
        });
        
        Console.WriteLine("Premi un tasto per uscire...");
        Console.ReadKey();
        cancellationToken.Cancel();
    }
}
```

## Estensioni Future

1. **Machine Learning**: Predire quando un processo potrebbe bloccarsi
2. **Integrazione con Cursor/VS Code**: Plugin per avvisare durante sviluppo
3. **Analisi Storica**: Database per tracciare pattern nel tempo
4. **Auto-fix**: Suggerire automaticamente comandi ottimizzati
5. **Dashboard Web**: Interfaccia web per monitoraggio remoto
6. **Rilevamento Processi Orfani**: Identificare e gestire processi rimasti attivi dopo la chiusura del parent process principale (es. sh.exe, git-credential-manager.exe)

## Note di Implementazione

- Usare `PerformanceCounter` per metriche accurate
- Usare `WMI` (Windows Management Instrumentation) per command line
- Implementare caching per evitare query eccessive
- Usare async/await per operazioni I/O
- Implementare timeout su tutte le operazioni potenzialmente lunghe
- Logging strutturato per debugging

