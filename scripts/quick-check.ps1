# Quick check per operazioni lente
# Uso: .\scripts\quick-check.ps1

Write-Host "Quick Check - Processi e operazioni..." -ForegroundColor Cyan

# Verifica rapida processi bloccati
$blocked = Get-Process python,pwsh,powershell -ErrorAction SilentlyContinue | 
    Where-Object {-not $_.Responding -or $_.CPU -gt 100}

if ($blocked) {
    Write-Host "⚠️  Processi potenzialmente bloccati:" -ForegroundColor Red
    $blocked | Format-Table ProcessName, Id, CPU, Responding -AutoSize
} else {
    Write-Host "✓ Nessun processo bloccato rilevato" -ForegroundColor Green
}

# Verifica dimensioni workspace (solo primo livello)
$workspaceFiles = Get-ChildItem -Path . -File -ErrorAction SilentlyContinue
$workspaceSize = ($workspaceFiles | Measure-Object -Property Length -Sum).Sum
Write-Host "`nWorkspace corrente: $($workspaceFiles.Count) file, $([math]::Round($workspaceSize/1MB,2)) MB" -ForegroundColor Gray

