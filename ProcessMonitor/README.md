# ProcessMonitor

Monitoraggio avanzato di processi bloccati e operazioni lente su Windows con integrazione di strumenti esterni.

## Funzionalità

- **Monitoraggio Processi in Tempo Reale**: Rileva processi bloccati, con CPU alto, memory leak e I/O eccessivo
- **Analisi Comandi**: Analizza comandi PowerShell/Python/Node per rilevare pattern pericolosi
- **Analisi Directory**: Identifica directory problematiche che potrebbero causare lentezza
- **Integrazione Strumenti Esterni**: Supporto per WhatIsHang, UIHang, Process Explorer e Procmon

## Requisiti

- .NET 8.0 SDK
- Windows (usa API specifiche di Windows)
- Strumenti esterni opzionali (configurabili in `appsettings.json`)

## Installazione

```bash
git clone https://github.com/slim16165/ProcessMonitor.git
cd ProcessMonitor/ProcessMonitor
dotnet restore
dotnet build
```

## Configurazione

Modifica `appsettings.json` per configurare:

- Soglie di monitoraggio (CPU, memoria, I/O)
- Processi sospetti da monitorare
- Percorsi degli strumenti esterni (WhatIsHang, UIHang, Process Explorer, Procmon)
- Directory da escludere durante l'analisi

## Utilizzo

```bash
dotnet run
```

### Comandi Disponibili

- `q` - Esci
- `s` - Analisi workspace
- `b` - Processi bloccati
- `a` - Tutti i processi
- `w` - Analizza con WhatIsHang (richiede PID)
- `u` - Analizza con UIHang (richiede PID)
- `e` - Apri Process Explorer (richiede PID)
- `p` - Apri Procmon
- `t` - Lista strumenti disponibili

## Strumenti Esterni Supportati

- **WhatIsHang**: Analizza processi bloccati e freeze UI
- **UIHang**: Rileva UI hang e freeze
- **Process Explorer**: Visualizza dettagli processi avanzati
- **Procmon**: Monitora attività file system, registry e network

## Architettura

- `ProcessMonitorService`: Monitoraggio principale dei processi
- `CommandAnalyzer`: Analisi pattern pericolosi nei comandi
- `DirectoryAnalyzer`: Analisi directory con timeout
- `PerformanceCollector`: Raccolta metriche CPU, memoria e I/O
- `ExternalToolsService`: Integrazione strumenti esterni

## Licenza

MIT

