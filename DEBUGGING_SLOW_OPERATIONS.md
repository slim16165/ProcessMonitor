# Guida: Individuare Operazioni Lente

## Problema Comune

Quando un comando PowerShell impiega troppo tempo, spesso è perché:
1. **Ricerca ricorsiva su directory troppo grandi** (es: Dropbox, node_modules)
2. **Processi bloccati in attesa di input**
3. **Operazioni I/O su migliaia di file**

## Individuazione Rapida

### 1. Quick Check (5 secondi)

```powershell
.\scripts\quick-check.ps1
```

Verifica rapidamente:
- Processi bloccati
- Dimensioni workspace corrente

### 2. Analisi Completa (30 secondi)

```powershell
.\scripts\check-slow-operations.ps1
```

Analisi dettagliata di:
- Processi con alto CPU
- Comandi PowerShell/Python attivi
- Dimensioni directory
- Operazioni I/O pesanti

### 3. Controllo Manuale Rapido

```powershell
# Verifica processi bloccati
Get-Process python,pwsh | Where-Object {-not $_.Responding}

# Verifica CPU alto
Get-Process | Where-Object {$_.CPU -gt 10} | Sort-Object CPU -Descending

# Verifica comandi PowerShell attivi
Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'pwsh.exe'} | Select-Object ProcessId, CommandLine
```

## Prevenzione

### ❌ SBAGLIATO - Cerca in tutto il sistema

```powershell
Get-ChildItem -Path C:\ -Recurse -File  # ⚠️ TROPPO LENTO!
```

### ✅ CORRETTO - Limita il percorso

```powershell
# Solo workspace corrente
Get-ChildItem -Path . -Recurse -File

# Con profondità limitata
Get-ChildItem -Path . -Recurse -Depth 3 -File

# Escludi directory grandi
Get-ChildItem -Path . -Recurse -File -Exclude node_modules,vendor,.git

# Con filtro per tipo
Get-ChildItem -Path . -Recurse -Filter *.php
```

### ✅ CORRETTO - Usa timeout per operazioni lunghe

```powershell
# Con timeout (PowerShell 7+)
$job = Start-Job { Get-ChildItem -Recurse }
Wait-Job $job -Timeout 5
if ($job.State -eq 'Running') {
    Stop-Job $job
    Write-Host "Operazione interrotta dopo 5 secondi"
}
```

## Segnali di Problema

### 🚨 Comando impiega > 10 secondi
- **Causa probabile**: Ricerca ricorsiva su directory grande
- **Soluzione**: Limita il percorso o usa -Depth

### 🚨 Processo con CPU > 100
- **Causa probabile**: Loop infinito o operazione pesante
- **Soluzione**: Verifica il comando con `Get-CimInstance Win32_Process`

### 🚨 Processo "Not Responding"
- **Causa probabile**: Bloccato in attesa di input
- **Soluzione**: Termina con `Stop-Process -Id <PID>`

### 🚨 Memoria in crescita costante
- **Causa probabile**: Memory leak o caricamento file troppo grandi
- **Soluzione**: Usa streaming invece di caricare tutto in memoria

## Best Practices

1. **Sempre limitare il percorso**: Usa `.` invece di `C:\`
2. **Usa -Depth**: Limita la profondità della ricerca ricorsiva
3. **Escludi directory grandi**: `-Exclude node_modules,vendor,.git`
4. **Usa filtri**: `-Filter *.php` invece di filtrare dopo
5. **Monitora il tempo**: Usa `Measure-Command` per vedere quanto impiega
6. **Interrompi con Ctrl+C**: Se un comando impiega troppo, interrompilo

## Esempi Pratici

### Contare file PHP nel workspace

```powershell
# ✅ Veloce - solo PHP nel workspace
(Get-ChildItem -Path . -Recurse -Filter *.php -File).Count

# ❌ Lento - cerca tutto e poi filtra
Get-ChildItem -Path . -Recurse -File | Where-Object {$_.Extension -eq '.php'} | Measure-Object
```

### Trovare file grandi

```powershell
# ✅ Veloce - con filtro e limite
Get-ChildItem -Path . -Recurse -File | 
    Where-Object {$_.Length -gt 10MB} | 
    Select-Object -First 10

# ❌ Lento - ordina tutto
Get-ChildItem -Path . -Recurse -File | 
    Sort-Object Length -Descending | 
    Select-Object -First 10
```

### Verificare se un comando è bloccato

```powershell
# Controlla se un processo sta usando CPU
$proc = Get-Process -Id <PID>
if ($proc.CPU -eq 0 -and -not $proc.Responding) {
    Write-Host "Processo probabilmente bloccato"
}
```

## Alias Utili

Aggiungi al tuo `$PROFILE`:

```powershell
# Quick check processi
function Check-Processes {
    Get-Process python,pwsh,powershell -ErrorAction SilentlyContinue | 
        Select-Object ProcessName, Id, CPU, Responding
}

# Verifica workspace
function Check-Workspace {
    $files = Get-ChildItem -Path . -File -ErrorAction SilentlyContinue
    $size = ($files | Measure-Object -Property Length -Sum).Sum
    Write-Host "File: $($files.Count), Size: $([math]::Round($size/1MB,2)) MB"
}
```

## Troubleshooting

### Problema: Comando PowerShell non risponde

1. Apri un nuovo terminale
2. Esegui `.\scripts\quick-check.ps1`
3. Se vedi processi bloccati, terminali con `Stop-Process -Id <PID>`

### Problema: Ricerca ricorsiva troppo lenta

1. Interrompi con Ctrl+C
2. Limita il percorso: `-Path .` invece di `-Path C:\`
3. Usa `-Depth 3` per limitare la profondità
4. Escludi directory grandi: `-Exclude node_modules,vendor`

### Problema: Processo Python bloccato

1. Verifica se è in attesa di input: `Get-CimInstance Win32_Process -Filter "ProcessId = <PID>"`
2. Controlla il comando eseguito
3. Se necessario, termina: `Stop-Process -Id <PID>`

