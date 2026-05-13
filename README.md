# ProcessMonitor

Monitoraggio avanzato di processi bloccati e operazioni lente su Windows con integrazione di strumenti esterni.

## Funzionalità

- **Monitoraggio Processi in Tempo Reale**: Rileva processi bloccati, con CPU alto, memory leak e I/O eccessivo
- **Analisi Comandi**: Analizza comandi PowerShell/Python/Node per rilevare pattern pericolosi
- **Analisi Directory**: Identifica directory problematiche che potrebbero causare lentezza
- **Integrazione Strumenti Esterni**: Supporto per WhatIsHang, UIHang, Process Explorer e Procmon
- **Process Tree Investigation**: Ricostruisce parent, child, provenance logica, owner e tag dei processi Windows
- **Remediation Planning**: Propone kill order prudente leaf-first per processi console/Git sospetti

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
- `i` - Inspect process tree
- `j` - Inspect process tree JSON
- `m` - Remediation dry-run
- `k` - Remediation apply

### Ownership E Tag

Il tool distingue tra:

- **Owner gerarchico**: ad esempio `IDE > Cursor`, `IDE > Windsurf`, `SCM > Git`, `Terminal > PowerShell`
- **Tag piatti**: ad esempio `git`, `git-network`, `blocked`, `tcp-active`, `leaf`, `owner-inherited`

Questo consente di raggruppare processi per owner logico e, separatamente, filtrare per comportamento o rischio.

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
- `ProcessSnapshotService`: Snapshot centralizzato dei processi Windows
- `ProcessTreeResolver`: Risoluzione dell'albero di processo
- `OwnerResolver`: Classificazione owner gerarchica
- `TagEnricher`: Tag comportamentali non gerarchici
- `RemediationPlanner`: Piano di remediation prudente

## Licenza

MIT

