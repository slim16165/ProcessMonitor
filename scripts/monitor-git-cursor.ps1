<#
.SYNOPSIS
    Monitor specifico per processi Git generati da Cursor e altri IDE.

.DESCRIPTION
    Monitora i processi Git attivi e identifica quale applicazione (Cursor, Visual Studio, ecc.)
    sta generando troppi processi Git. Avvisa quando si superano le soglie di sicurezza.

.PARAMETER Watch
    Attiva modalità watch continua (refresh ogni 5 secondi)

.PARAMETER Threshold
    Soglia di allarme per numero di processi Git (default: 15)

.PARAMETER AutoKill
    Termina automaticamente i processi Git quando superano la soglia (default: false)

.EXAMPLE
    .\monitor-git-cursor.ps1
    Mostra un report dei processi Git attivi

.EXAMPLE
    .\monitor-git-cursor.ps1 -Watch
    Monitoraggio continuo con refresh automatico

.EXAMPLE
    .\monitor-git-cursor.ps1 -Threshold 20 -AutoKill
    Termina automaticamente i processi quando superano 20
#>

param(
    [switch]$Watch,
    [int]$Threshold = 15,
    [switch]$AutoKill
)

# ============================================
#  CONFIGURAZIONE
# ============================================
$refreshInterval = 5  # secondi per modalità watch
$warningThreshold = 10  # soglia di warning (giallo)
$criticalThreshold = $Threshold  # soglia critica (rosso)

# ============================================
#  FUNZIONI
# ============================================

function Get-GitProcessesWithParent {
    <#
    Ottiene tutti i processi git con informazioni sul processo padre
    #>
    $gitProcesses = Get-Process | Where-Object {$_.ProcessName -eq "git"} -ErrorAction SilentlyContinue
    
    if (-not $gitProcesses) {
        return @()
    }
    
    $result = @()
    foreach ($proc in $gitProcesses) {
        try {
            $parentProc = Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
            $parentId = $parentProc.ParentProcessId
            
            $parentInfo = $null
            if ($parentId) {
                try {
                    $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue
                    $parentInfo = @{
                        Id = $parentId
                        Name = $parent.ProcessName
                        Path = $parent.Path
                    }
                } catch {
                    $parentInfo = @{
                        Id = $parentId
                        Name = "Unknown"
                        Path = "N/A"
                    }
                }
            }
            
            $runtime = (Get-Date) - $proc.StartTime
            $memMB = [math]::Round($proc.WorkingSet64 / 1MB, 2)
            
            $result += [PSCustomObject]@{
                GitPID = $proc.Id
                StartTime = $proc.StartTime
                Runtime = $runtime
                RuntimeMinutes = [math]::Round($runtime.TotalMinutes, 2)
                CPU = $proc.CPU
                MemoryMB = $memMB
                ParentPID = $parentInfo.Id
                ParentName = $parentInfo.Name
                ParentPath = $parentInfo.Path
            }
        } catch {
            # Ignora errori
        }
    }
    
    return $result
}

function Get-GitProcessesByParent {
    <#
    Raggruppa i processi git per processo padre
    #>
    $allProcesses = Get-GitProcessesWithParent
    
    $grouped = $allProcesses | Group-Object -Property ParentName
    
    $result = @()
    foreach ($group in $grouped) {
        $parentName = $group.Name
        $count = $group.Count
        
        # Calcola statistiche
        $avgRuntime = ($group.Group | Measure-Object -Property RuntimeMinutes -Average).Average
        $totalMemory = ($group.Group | Measure-Object -Property MemoryMB -Sum).Sum
        
        $status = "OK"
        $color = "Green"
        if ($count -ge $criticalThreshold) {
            $status = "CRITICO"
            $color = "Red"
        } elseif ($count -ge $warningThreshold) {
            $status = "WARNING"
            $color = "Yellow"
        }
        
        $result += [PSCustomObject]@{
            ParentProcess = $parentName
            GitProcessCount = $count
            AvgRuntimeMinutes = [math]::Round($avgRuntime, 2)
            TotalMemoryMB = [math]::Round($totalMemory, 2)
            Status = $status
            StatusColor = $color
            Processes = $group.Group
        }
    }
    
    return $result | Sort-Object -Property GitProcessCount -Descending
}

function Show-GitProcessReport {
    <#
    Mostra un report completo dei processi git
    #>
    Clear-Host
    Write-Host "=== MONITOR PROCESSI GIT - CURSOR/IDE ===" -ForegroundColor Cyan
    Write-Host "Ora: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray
    Write-Host ""
    
    $allProcesses = Get-GitProcessesWithParent
    $totalCount = $allProcesses.Count
    
    # Statistiche generali
    Write-Host "[STATISTICHE GENERALI]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    
    if ($totalCount -eq 0) {
        Write-Host "✓ Nessun processo Git attivo" -ForegroundColor Green
        Write-Host ""
        return
    }
    
    $statusColor = "Green"
    $statusText = "OK"
    if ($totalCount -ge $criticalThreshold) {
        $statusColor = "Red"
        $statusText = "CRITICO"
    } elseif ($totalCount -ge $warningThreshold) {
        $statusColor = "Yellow"
        $statusText = "WARNING"
    }
    
    Write-Host "Processi Git totali: $totalCount" -ForegroundColor $statusColor
    Write-Host "Stato: $statusText" -ForegroundColor $statusColor
    Write-Host ""
    
    # Raggruppamento per processo padre
    Write-Host "[PROCESSI GIT PER APPLICAZIONE PADRE]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host ""
    
    $byParent = Get-GitProcessesByParent
    
    if ($byParent.Count -eq 0) {
        Write-Host "Nessun processo padre identificato" -ForegroundColor Yellow
        Write-Host ""
    } else {
        foreach ($parent in $byParent) {
            Write-Host "Processo padre: $($parent.ParentProcess)" -ForegroundColor White
            Write-Host "  Processi Git generati: $($parent.GitProcessCount)" -ForegroundColor $parent.StatusColor
            Write-Host "  Memoria totale: $($parent.TotalMemoryMB) MB" -ForegroundColor Gray
            Write-Host "  Runtime medio: $($parent.AvgRuntimeMinutes) minuti" -ForegroundColor Gray
            Write-Host "  Stato: $($parent.Status)" -ForegroundColor $parent.StatusColor
            
            # Mostra dettagli se critico o warning
            if ($parent.GitProcessCount -ge $warningThreshold) {
                Write-Host ""
                Write-Host "  Dettagli processi Git:" -ForegroundColor Yellow
                $parent.Processes | Select-Object -First 5 | ForEach-Object {
                    Write-Host "    PID: $($_.GitPID) | Runtime: $($_.RuntimeMinutes) min | Mem: $($_.MemoryMB) MB" -ForegroundColor DarkGray
                }
                if ($parent.Processes.Count -gt 5) {
                    Write-Host "    ... e altri $($parent.Processes.Count - 5) processi" -ForegroundColor DarkGray
                }
            }
            Write-Host ""
        }
    }
    
    # Processi git senza padre identificato
    $orphans = $allProcesses | Where-Object {-not $_.ParentName -or $_.ParentName -eq "Unknown"}
    if ($orphans.Count -gt 0) {
        Write-Host "[PROCESSI GIT SENZA PADRE IDENTIFICATO]" -ForegroundColor Yellow
        Write-Host "=" * 60 -ForegroundColor Yellow
        Write-Host "Trovati $($orphans.Count) processi Git senza processo padre identificato" -ForegroundColor Yellow
        Write-Host ""
    }
    
    # Suggerimenti
    if ($totalCount -ge $criticalThreshold) {
        Write-Host "[AZIONI CONSIGLIATE]" -ForegroundColor Red
        Write-Host "=" * 60 -ForegroundColor Red
        Write-Host "⚠️  TROPPI PROCESSI GIT ATTIVI!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Soluzioni:" -ForegroundColor Yellow
        Write-Host "  1. Esegui: .\fix-git-blocked.ps1 -Force" -ForegroundColor White
        Write-Host "  2. Riavvia Cursor/Visual Studio" -ForegroundColor White
        Write-Host "  3. Disabilita temporaneamente git in Cursor (Settings > git.enabled)" -ForegroundColor White
        Write-Host "  4. Usa terminale esterno per comandi git complessi" -ForegroundColor White
        Write-Host ""
        
        if ($AutoKill) {
            Write-Host "[AUTO-KILL ATTIVO]" -ForegroundColor Red
            Write-Host "Terminazione automatica dei processi Git..." -ForegroundColor Yellow
            Stop-GitProcesses -Force
        }
    }
}

function Stop-GitProcesses {
    <#
    Termina tutti i processi git attivi
    #>
    param([switch]$Force)
    
    $processes = Get-Process | Where-Object {$_.ProcessName -eq "git"} -ErrorAction SilentlyContinue
    
    if (-not $processes) {
        Write-Host "Nessun processo Git da terminare" -ForegroundColor Green
        return
    }
    
    $count = 0
    foreach ($proc in $processes) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            $count++
        } catch {
            Write-Host "Errore terminando processo $($proc.Id): $_" -ForegroundColor Red
        }
    }
    
    Write-Host "Terminati $count processi Git" -ForegroundColor Green
}

# ============================================
#  ESECUZIONE PRINCIPALE
# ============================================

if ($Watch) {
    Write-Host "Modalità WATCH attiva (refresh ogni $refreshInterval secondi)" -ForegroundColor Cyan
    Write-Host "Premi Ctrl+C per interrompere" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        while ($true) {
            Show-GitProcessReport
            Start-Sleep -Seconds $refreshInterval
        }
    } catch {
        Write-Host "`nMonitoraggio interrotto" -ForegroundColor Yellow
    }
} else {
    Show-GitProcessReport
}



