# Script per individuare operazioni lente e processi bloccati
# Uso: .\scripts\check-slow-operations.ps1

Write-Host "=== CONTROLLO OPERAZIONI LENTE ===" -ForegroundColor Cyan
Write-Host ""

# 1. Verifica processi con alto utilizzo CPU
Write-Host "1. Processi con alto utilizzo CPU (>10%):" -ForegroundColor Yellow
Get-Process | Where-Object {$_.CPU -gt 10} | 
    Select-Object ProcessName, Id, @{Name="CPU";Expression={$_.CPU}}, 
                @{Name="MemoryMB";Expression={[math]::Round($_.WorkingSet/1MB,2)}}, 
                StartTime | 
    Sort-Object CPU -Descending | 
    Format-Table -AutoSize

Write-Host ""

# 2. Verifica processi PowerShell/Python bloccati
Write-Host "2. Processi PowerShell/Python attivi:" -ForegroundColor Yellow
$suspiciousProcesses = Get-Process python,pwsh,powershell -ErrorAction SilentlyContinue
if ($suspiciousProcesses) {
    $suspiciousProcesses | 
        Select-Object ProcessName, Id, @{Name="CPU";Expression={$_.CPU}}, 
                    Responding, StartTime | 
        Format-Table -AutoSize
    
    Write-Host "`n   Comandi in esecuzione:" -ForegroundColor Gray
    foreach ($proc in $suspiciousProcesses) {
        try {
            $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)").CommandLine
            if ($cmdLine) {
                Write-Host "   PID $($proc.Id): $($cmdLine.Substring(0, [Math]::Min(100, $cmdLine.Length)))" -ForegroundColor Gray
            }
        } catch {}
    }
} else {
    Write-Host "   Nessun processo sospetto trovato" -ForegroundColor Green
}

Write-Host ""

# 3. Verifica dimensioni directory workspace corrente
Write-Host "3. Analisi workspace corrente:" -ForegroundColor Yellow
$workspacePath = Get-Location
Write-Host "   Path: $workspacePath" -ForegroundColor Gray

# Conta file e dimensioni (con timeout)
$fileCount = 0
$totalSize = 0
$startTime = Get-Date

try {
    Get-ChildItem -Path . -Recurse -File -ErrorAction SilentlyContinue | 
        ForEach-Object {
            $fileCount++
            $totalSize += $_.Length
            # Timeout dopo 5 secondi
            if ((Get-Date) - $startTime -gt [TimeSpan]::FromSeconds(5)) {
                Write-Host "   ⚠️  Analisi interrotta dopo 5 secondi (troppi file?)" -ForegroundColor Red
                throw "Timeout"
            }
        }
    
    Write-Host "   File trovati: $fileCount" -ForegroundColor Green
    Write-Host "   Dimensione totale: $([math]::Round($totalSize/1MB,2)) MB" -ForegroundColor Green
} catch {
    Write-Host "   ⚠️  Errore durante l'analisi: $_" -ForegroundColor Red
    Write-Host "   💡 Probabile che ci siano troppi file o directory troppo grandi" -ForegroundColor Yellow
}

Write-Host ""

# 4. Verifica se ci sono operazioni I/O pesanti
Write-Host "4. Processi con alto I/O:" -ForegroundColor Yellow
Get-Counter "\Process(*)\IO Data Bytes/sec" -ErrorAction SilentlyContinue | 
    Select-Object -ExpandProperty CounterSamples | 
    Where-Object {$_.CookedValue -gt 1000000} | 
    Sort-Object CookedValue -Descending | 
    Select-Object -First 10 |
    Format-Table InstanceName, @{Name="IO_MB_per_sec";Expression={[math]::Round($_.CookedValue/1MB,2)}} -AutoSize

Write-Host ""

# 5. Suggerimenti
Write-Host "=== SUGGERIMENTI ===" -ForegroundColor Cyan
Write-Host "• Se un comando impiega troppo tempo, usa Ctrl+C per interromperlo" -ForegroundColor White
Write-Host "• Per ricerche ricorsive, limita sempre il path (es: -Path . invece di -Path C:\)" -ForegroundColor White
Write-Host "• Usa -Depth per limitare la profondità della ricerca ricorsiva" -ForegroundColor White
Write-Host "• Per file grandi, usa -Filter o -Include per limitare i risultati" -ForegroundColor White
Write-Host "• Considera di escludere node_modules, vendor, .git con -Exclude" -ForegroundColor White

