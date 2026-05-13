<#
.SYNOPSIS
    Monitor per processi Git attivi e verifica repository Git problematici.

.DESCRIPTION
    - Monitora i processi Git in esecuzione
    - Verifica se ci sono operazioni Git che stanno processando troppi file
    - Controlla repository Git grandi o problematici
    - Identifica operazioni Git bloccate o lente
#>

# ============================================
#  CONFIGURAZIONE
# ============================================
$maxFileCount = 10000  # Soglia per considerare un repository "grande"
$maxRepoSizeMB = 500   # Soglia in MB per considerare un repository grande
$checkInterval = 2     # Secondi tra i controlli (per modalità watch)

# ============================================
#  FUNZIONI DI MONITORAGGIO
# ============================================

function Get-GitProcesses {
    Write-Host "`n[GIT PROCESSES]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    
    $gitProcs = Get-Process -Name "git*" -ErrorAction SilentlyContinue | 
        Where-Object { $_.ProcessName -match "^git" }
    
    if ($gitProcs.Count -eq 0) {
        Write-Host "Nessun processo Git attivo trovato." -ForegroundColor Green
        return @()
    }
    
    Write-Host "Trovati $($gitProcs.Count) processo/i Git attivo/i:" -ForegroundColor Yellow
    Write-Host ""
    
    foreach ($proc in $gitProcs) {
        $cpu = $proc.CPU
        $memMB = [math]::Round($proc.WorkingSet64 / 1MB, 2)
        $runtime = (Get-Date) - $proc.StartTime
        
        $status = "OK"
        $color = "Green"
        
        # Avviso se il processo usa troppa CPU o memoria
        if ($cpu -gt 50 -or $memMB -gt 500) {
            $status = "ATTENZIONE: Alto utilizzo risorse"
            $color = "Red"
        } elseif ($runtime.TotalMinutes -gt 5) {
            $status = "ATTENZIONE: Processo in esecuzione da più di 5 minuti"
            $color = "Yellow"
        }
        
        Write-Host "PID: $($proc.Id) | CPU: $cpu% | Memoria: ${memMB}MB | Runtime: $($runtime.ToString('mm\:ss'))" -ForegroundColor White
        Write-Host "  Status: $status" -ForegroundColor $color
        
        # Prova a ottenere la command line
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)").CommandLine
            if ($cmdLine) {
                Write-Host "  Comando: $cmdLine" -ForegroundColor DarkGray
            }
        } catch {
            # Ignora errori nel recupero della command line
        }
        Write-Host ""
    }
    
    return $gitProcs
}

function Find-LargeGitRepositories {
    param(
        [string]$RootPath = "C:\Users\g.salvi",
        [int]$MaxDepth = 5
    )
    
    Write-Host "`n[REPOSITORY GIT GRANDI]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    
    $repos = @()
    $checked = 0
    
    function Search-GitRepos {
        param(
            [string]$Path,
            [int]$Depth
        )
        
        if ($Depth -gt $MaxDepth) { return }
        
        $checked++
        if ($checked % 50 -eq 0) {
            Write-Host "  Controllate $checked directory..." -ForegroundColor DarkGray
        }
        
        try {
            # Cerca cartelle .git
            $gitDirs = Get-ChildItem -Path $Path -Filter ".git" -Directory -Force -ErrorAction SilentlyContinue
            
            foreach ($gitDir in $gitDirs) {
                $repoPath = $gitDir.Parent.FullName
                
                # Verifica se è un repository Git valido
                Push-Location $repoPath -ErrorAction SilentlyContinue | Out-Null
                $isValid = git rev-parse --git-dir 2>$null
                Pop-Location | Out-Null
                
                if ($isValid) {
                    # Calcola dimensione del repository
                    $repoSize = (Get-ChildItem -Path $gitDir.FullName -Recurse -File -ErrorAction SilentlyContinue | 
                        Measure-Object -Property Length -Sum).Sum / 1MB
                    
                    # Conta file tracciati (approssimativo)
                    Push-Location $repoPath -ErrorAction SilentlyContinue | Out-Null
                    $fileCount = (git ls-files 2>$null | Measure-Object -Line).Lines
                    Pop-Location | Out-Null
                    
                    $repos += [PSCustomObject]@{
                        Path = $repoPath
                        SizeMB = [math]::Round($repoSize, 2)
                        FileCount = $fileCount
                        GitDir = $gitDir.FullName
                    }
                }
            }
            
            # Cerca ricorsivamente (ma non nelle cartelle .git)
            $subDirs = Get-ChildItem -Path $Path -Directory -Force -ErrorAction SilentlyContinue | 
                Where-Object { $_.Name -ne ".git" -and $_.Name -notlike ".*" }
            
            foreach ($subDir in $subDirs) {
                Search-GitRepos -Path $subDir.FullName -Depth ($Depth + 1)
            }
        } catch {
            # Ignora errori di accesso
        }
    }
    
    Write-Host "Cerca repository Git in $RootPath (max depth: $MaxDepth)..." -ForegroundColor Yellow
    Search-GitRepos -Path $RootPath -Depth 0
    
    Write-Host "`nTrovati $($repos.Count) repository Git:" -ForegroundColor Yellow
    Write-Host ""
    
    if ($repos.Count -eq 0) {
        Write-Host "Nessun repository Git trovato." -ForegroundColor Green
        return
    }
    
    # Ordina per dimensione/file count
    $repos = $repos | Sort-Object -Property SizeMB, FileCount -Descending
    
    foreach ($repo in $repos) {
        $warning = ""
        $color = "White"
        
        if ($repo.FileCount -gt $maxFileCount) {
            $warning = " [ATTENZIONE: TROPPI FILE]"
            $color = "Red"
        } elseif ($repo.SizeMB -gt $maxRepoSizeMB) {
            $warning = " [ATTENZIONE: TROPPO GRANDE]"
            $color = "Yellow"
        }
        
        Write-Host "Repository: $($repo.Path)" -ForegroundColor $color
        Write-Host "  Dimensione: $($repo.SizeMB) MB | File tracciati: $($repo.FileCount)$warning" -ForegroundColor $color
        Write-Host ""
    }
    
    return $repos
}

function Test-GitOperations {
    Write-Host "`n[OPERAZIONI GIT PROBLEMATICHE]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    
    # Verifica se ci sono operazioni Git bloccate
    $gitProcs = Get-Process -Name "git*" -ErrorAction SilentlyContinue
    
    if ($gitProcs.Count -eq 0) {
        Write-Host "Nessuna operazione Git attiva." -ForegroundColor Green
        return
    }
    
    Write-Host "Verifica operazioni Git attive..." -ForegroundColor Yellow
    
    foreach ($proc in $gitProcs) {
        $runtime = (Get-Date) - $proc.StartTime
        
        if ($runtime.TotalMinutes -gt 10) {
            Write-Host "ATTENZIONE: Processo Git PID $($proc.Id) in esecuzione da $([math]::Round($runtime.TotalMinutes, 1)) minuti" -ForegroundColor Red
            Write-Host "  Potrebbe essere bloccato o sta processando troppi file!" -ForegroundColor Red
        }
    }
}

function Show-GitStatus {
    Write-Host "`n[STATO GENERALE GIT]" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    
    # Verifica configurazione Git globale
    $gitConfig = git config --global --list 2>$null
    if ($gitConfig) {
        Write-Host "Configurazione Git globale trovata:" -ForegroundColor Yellow
        $gitConfig | Select-Object -First 5 | ForEach-Object {
            Write-Host "  $_" -ForegroundColor DarkGray
        }
        if ($gitConfig.Count -gt 5) {
            Write-Host "  ... e altre $($gitConfig.Count - 5) configurazioni" -ForegroundColor DarkGray
        }
    }
    
    # Verifica se c'è un repository nella home
    $homeGit = "C:\Users\g.salvi\.git"
    if (Test-Path $homeGit) {
        if (Test-Path $homeGit -PathType Container) {
            Write-Host "`nATTENZIONE: Trovata cartella .git nella home!" -ForegroundColor Red
            Write-Host "  Questo può causare problemi di performance." -ForegroundColor Red
        } else {
            Write-Host "`nProtezione attiva: file .git presente nella home (previene inizializzazione)" -ForegroundColor Green
        }
    }
}

# ============================================
#  MENU PRINCIPALE
# ============================================
function Show-Menu {
    Clear-Host
    Write-Host "===================================================" -ForegroundColor Cyan
    Write-Host "     Git Monitor - Performance & Process Check     " -ForegroundColor Yellow
    Write-Host "===================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Mostra processi Git attivi"
    Write-Host "2. Cerca repository Git grandi"
    Write-Host "3. Verifica operazioni Git problematiche"
    Write-Host "4. Mostra stato generale Git"
    Write-Host "5. Esegui tutti i controlli"
    Write-Host "6. Modalità watch (monitoraggio continuo)"
    Write-Host "0. Esci"
    Write-Host ""
}

do {
    Show-Menu
    $choice = Read-Host "Scegli un'opzione"
    
    switch ($choice) {
        0 {
            Write-Host "Uscita..." -ForegroundColor Yellow
            break
        }
        1 {
            Get-GitProcesses | Out-Null
            Read-Host "`nPremi Invio per continuare..."
        }
        2 {
            $root = Read-Host "Inserisci il percorso radice da controllare (default: C:\Users\g.salvi)"
            if ([string]::IsNullOrWhiteSpace($root)) {
                $root = "C:\Users\g.salvi"
            }
            Find-LargeGitRepositories -RootPath $root | Out-Null
            Read-Host "`nPremi Invio per continuare..."
        }
        3 {
            Test-GitOperations
            Read-Host "`nPremi Invio per continuare..."
        }
        4 {
            Show-GitStatus
            Read-Host "`nPremi Invio per continuare..."
        }
        5 {
            Show-GitStatus
            Get-GitProcesses | Out-Null
            Test-GitOperations
            Find-LargeGitRepositories | Out-Null
            Read-Host "`nPremi Invio per continuare..."
        }
        6 {
            Write-Host "Modalità watch attiva. Premi Ctrl+C per interrompere..." -ForegroundColor Yellow
            Write-Host ""
            while ($true) {
                Clear-Host
                Write-Host "=== Git Monitor - Watch Mode ===" -ForegroundColor Cyan
                Write-Host "Ora: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor DarkGray
                Write-Host ""
                Get-GitProcesses | Out-Null
                Test-GitOperations
                Start-Sleep -Seconds $checkInterval
            }
        }
        default {
            Write-Host "Scelta non valida. Riprova." -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
} while ($choice -ne "0")

