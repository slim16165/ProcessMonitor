<#
.SYNOPSIS
    Verifica le impostazioni di Cursor relative a Git e suggerisce ottimizzazioni.

.DESCRIPTION
    Controlla le impostazioni di Cursor che possono influenzare il comportamento di Git
    e suggerisce modifiche per prevenire problemi di performance.

.EXAMPLE
    .\check-cursor-git-settings.ps1
    Mostra le impostazioni attuali e suggerimenti
#>

Write-Host "=== VERIFICA IMPOSTAZIONI CURSOR/GIT ===" -ForegroundColor Cyan
Write-Host ""

# Percorsi comuni delle impostazioni Cursor
$cursorSettingsPaths = @(
    "$env:APPDATA\Cursor\User\settings.json",
    "$env:LOCALAPPDATA\Programs\cursor\resources\app\product.json"
)

Write-Host "[IMPOSTAZIONI CURSOR]" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""

# Verifica se Cursor è installato
$cursorProcess = Get-Process | Where-Object {$_.ProcessName -eq "Cursor"} -ErrorAction SilentlyContinue
if ($cursorProcess) {
    Write-Host "✓ Cursor è in esecuzione" -ForegroundColor Green
    Write-Host "  Processi attivi: $($cursorProcess.Count)" -ForegroundColor Gray
} else {
    Write-Host "⚠ Cursor non è in esecuzione" -ForegroundColor Yellow
}

Write-Host ""

# Verifica file di impostazioni
$settingsFound = $false
foreach ($path in $cursorSettingsPaths) {
    if (Test-Path $path) {
        Write-Host "✓ Trovato file impostazioni: $path" -ForegroundColor Green
        $settingsFound = $true
        
        try {
            $content = Get-Content $path -Raw -ErrorAction Stop
            $settings = $content | ConvertFrom-Json -ErrorAction SilentlyContinue
            
            if ($settings) {
                Write-Host "  Impostazioni caricate correttamente" -ForegroundColor Gray
                
                # Verifica impostazioni Git specifiche
                $gitSettings = @(
                    "git.enabled",
                    "git.autoRepositoryDetection",
                    "git.autorefresh",
                    "git.autoStash",
                    "git.confirmSync",
                    "git.enableSmartCommit"
                )
                
                Write-Host ""
                Write-Host "  Impostazioni Git rilevate:" -ForegroundColor Yellow
                foreach ($setting in $gitSettings) {
                    $value = $settings.$setting
                    if ($null -ne $value) {
                        $color = if ($value -eq $false -and $setting -eq "git.enabled") { "Green" } else { "White" }
                        Write-Host "    $setting = $value" -ForegroundColor $color
                    }
                }
            }
        } catch {
            Write-Host "  ⚠ Impossibile leggere impostazioni: $_" -ForegroundColor Yellow
        }
    }
}

if (-not $settingsFound) {
    Write-Host "⚠ File impostazioni Cursor non trovato nei percorsi standard" -ForegroundColor Yellow
    Write-Host "  I file di impostazioni potrebbero essere in un'altra posizione" -ForegroundColor Gray
}

Write-Host ""
Write-Host "[SUGGERIMENTI IMPOSTAZIONI]" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""

Write-Host "Per ottimizzare Cursor e prevenire problemi Git:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Apri Cursor e vai su:" -ForegroundColor White
Write-Host "   File > Preferences > Settings (o Ctrl+,)" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Cerca e modifica queste impostazioni:" -ForegroundColor White
Write-Host ""
Write-Host "   git.enabled" -ForegroundColor Cyan
Write-Host "   - Valore consigliato: true (disabilita solo se necessario)" -ForegroundColor Gray
Write-Host "   - Disabilita completamente l'integrazione Git" -ForegroundColor Gray
Write-Host ""
Write-Host "   git.autorefresh" -ForegroundColor Cyan
Write-Host "   - Valore consigliato: false" -ForegroundColor Gray
Write-Host "   - Disabilita il refresh automatico dello stato Git" -ForegroundColor Gray
Write-Host ""
Write-Host "   git.autoRepositoryDetection" -ForegroundColor Cyan
Write-Host "   - Valore consigliato: false" -ForegroundColor Gray
Write-Host "   - Disabilita la rilevazione automatica dei repository" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Per problemi con il terminale:" -ForegroundColor White
Write-Host "   Agents > Inline Editing & Terminal > Legacy Terminal Tool" -ForegroundColor Gray
Write-Host "   - Prova ad attivare/disattivare questa opzione" -ForegroundColor Gray
Write-Host ""

Write-Host "[VERIFICA PROCESSI GIT CORRENTI]" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""

$gitProcesses = Get-Process | Where-Object {$_.ProcessName -eq "git"} -ErrorAction SilentlyContinue
$gitCount = if ($gitProcesses) { $gitProcesses.Count } else { 0 }

Write-Host "Processi Git attivi: $gitCount" -ForegroundColor $(if ($gitCount -gt 15) { "Red" } elseif ($gitCount -gt 10) { "Yellow" } else { "Green" })

if ($gitCount -gt 15) {
    Write-Host ""
    Write-Host "⚠ ATTENZIONE: Troppi processi Git attivi!" -ForegroundColor Red
    Write-Host "  Esegui: .\kill-git-blocked.ps1 -Force" -ForegroundColor Yellow
} elseif ($gitCount -gt 10) {
    Write-Host ""
    Write-Host "⚠ Warning: Numero elevato di processi Git" -ForegroundColor Yellow
    Write-Host "  Monitora con: .\monitor-git-cursor.ps1 -Watch" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[COMANDI UTILI]" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "Monitoraggio continuo:" -ForegroundColor White
Write-Host "  .\monitor-git-cursor.ps1 -Watch" -ForegroundColor Gray
Write-Host ""
Write-Host "Terminazione automatica:" -ForegroundColor White
Write-Host "  .\kill-git-blocked.ps1 -Threshold 10 -Force" -ForegroundColor Gray
Write-Host ""
Write-Host "Fix completo:" -ForegroundColor White
Write-Host "  ..\..\fix-git-blocked.ps1 -Force" -ForegroundColor Gray
Write-Host ""

Write-Host "=== Verifica completata ===" -ForegroundColor Cyan



