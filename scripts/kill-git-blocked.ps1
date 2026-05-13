<#
.SYNOPSIS
    Termina automaticamente i processi Git bloccati o eccessivi.

.DESCRIPTION
    Identifica e termina i processi Git che:
    - Superano una soglia numerica
    - Sono in esecuzione da troppo tempo
    - Sono orfani (parent non piu attivo)

.PARAMETER Threshold
    Numero massimo di processi Git consentiti (default: 10)

.PARAMETER MaxRuntimeMinutes
    Tempo massimo di esecuzione in minuti prima di terminare (default: 10)
#>

param(
    [int]$Threshold = 10,
    [int]$MaxRuntimeMinutes = 10,
    [string]$ParentProcess = "",
    [switch]$DryRun,
    [switch]$Force
)

# ============================================
#  FUNZIONI
# ============================================

function Get-GitProcessesDetailed {
    <#
    Ottiene processi Git e correlati (sh, credential-manager) con dettagli sul processo padre
    #>
    $processNames = @("git", "sh", "git-credential-manager", "git-remote-https")
    $allProcs = Get-Process | Where-Object {$processNames -contains $_.ProcessName} -ErrorAction SilentlyContinue
    
    if (-not $allProcs) {
        return @()
    }
    
    $result = @()
    foreach ($proc in $allProcs) {
        try {
            $winProc = Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
            $parentId = 0
            if ($winProc) { $parentId = $winProc.ParentProcessId }
            
            $parentName = "Unknown"
            if ($parentId) {
                try {
                    $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue
                    if ($parent) { $parentName = $parent.ProcessName }
                } catch {}
            }
            
            $runtime = (Get-Date) - $proc.StartTime
            
            $isOrphan = $false
            if ($parentId) {
                $parentAlive = Get-Process -Id $parentId -ErrorAction SilentlyContinue
                if (-not $parentAlive) {
                    $isOrphan = $true
                }
            }

            $result += [PSCustomObject]@{
                PID = $proc.Id
                ProcessName = $proc.ProcessName
                StartTime = $proc.StartTime
                Runtime = $runtime
                RuntimeMinutes = [math]::Round($runtime.TotalMinutes, 2)
                CPU = $proc.CPU
                MemoryMB = [math]::Round($proc.WorkingSet64 / 1MB, 2)
                ParentPID = $parentId
                ParentName = $parentName
                IsOrphan = $isOrphan
                ShouldKill = $false
                Reason = ""
            }
        } catch {
            # Ignora errori
        }
    }
    
    return $result
}

function Find-ProcessesToKill {
    param(
        [array]$Processes,
        [int]$Threshold,
        [int]$MaxRuntimeMinutes,
        [string]$ParentFilter
    )
    
    $toKill = @()
    
    # Filtra per processo padre se specificato
    $filtered = if ($ParentFilter) {
        $Processes | Where-Object {$_.ParentName -eq $ParentFilter}
    } else {
        $Processes
    }
    
    # Marca processi orfani
    foreach ($proc in $filtered) {
        if ($proc.IsOrphan -and -not $proc.ShouldKill) {
            $proc.ShouldKill = $true
            $proc.Reason = "Orfano (Parent PID $($proc.ParentPID) terminato)"
            $toKill += $proc
        }
    }

    # Marca processi in esecuzione da troppo tempo
    foreach ($proc in $filtered) {
        if ($proc.RuntimeMinutes -gt $MaxRuntimeMinutes -and -not $proc.ShouldKill) {
            $proc.ShouldKill = $true
            $proc.Reason = "Runtime eccessivo ($($proc.RuntimeMinutes) min)"
            $toKill += $proc
        }
    }
    
    # Se dopo gli orfani e i timeout superiamo ancora la soglia
    $remaining = $filtered | Where-Object { -not $_.ShouldKill }
    if ($remaining.Count -gt $Threshold) {
        foreach ($proc in $remaining) {
            $proc.ShouldKill = $true
            $proc.Reason = "Supera soglia ($($filtered.Count) > $Threshold)"
            $toKill += $proc
        }
    }
    
    return $toKill
}

function Show-KillReport {
    param(
        [array]$ToKill,
        [switch]$DryRun
    )
    
    Write-Host "=== REPORT PROCESSI DA TERMINARE ===" -ForegroundColor Cyan
    
    if ($ToKill.Count -eq 0) {
        Write-Host "Nessun processo da terminare." -ForegroundColor Green
        return $false
    }
    
    $byReason = $ToKill | Group-Object -Property Reason
    foreach ($group in $byReason) {
        Write-Host "Motivo: $($group.Name) ($($group.Count) processi)" -ForegroundColor Yellow
        $group.Group | Select-Object -First 3 | ForEach-Object {
            Write-Host "  PID: $($_.PID) | Nome: $($_.ProcessName) | Parent: $($_.ParentName)" -ForegroundColor Gray
        }
    }
    
    if ($DryRun) {
        Write-Host "[DRY RUN] Nessuna azione eseguita." -ForegroundColor Yellow
        return $false
    }
    
    return $true
}

function Kill-Processes {
    param([array]$Processes)
    foreach ($proc in $Processes) {
        try {
            Stop-Process -Id $proc.PID -Force -ErrorAction Stop
            Write-Host "Terminato PID $($proc.PID) ($($proc.ProcessName))" -ForegroundColor Green
        } catch {
            Write-Host "Errore su PID $($proc.PID): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# ESECUZIONE
$procs = Get-GitProcessesDetailed
$toKill = Find-ProcessesToKill -Processes $procs -Threshold $Threshold -MaxRuntimeMinutes $MaxRuntimeMinutes -ParentFilter $ParentProcess

$shouldProceed = Show-KillReport -ToKill $toKill -DryRun $DryRun

if ($shouldProceed -and -not $Force -and -not $DryRun) {
    $resp = Read-Host "Confermi terminazione? (y/n)"
    if ($resp -ne "y") { exit }
}

if ($shouldProceed -and -not $DryRun) {
    Kill-Processes -Processes $toKill
}
