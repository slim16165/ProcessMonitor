# Strumenti per Gestione Processi Git Bloccati

Questa cartella contiene strumenti specifici per monitorare e gestire i processi Git bloccati causati da Cursor e altri IDE.

## Problema

Cursor (e altri IDE) possono generare molti processi Git contemporaneamente, causando:
- Blocchi del sistema
- Utilizzo eccessivo di risorse
- Impossibilità di eseguire operazioni Git

Questo è un [problema noto di Cursor](https://forum.cursor.com/t/cursor-spawns-hundreds-of-rg-processes/135818).

## Strumenti Disponibili

### 1. `monitor-git-cursor.ps1`
Monitor specifico per processi Git generati da Cursor e altri IDE.

**Uso:**
```powershell
# Report una tantum
.\monitor-git-cursor.ps1

# Monitoraggio continuo (refresh ogni 5 secondi)
.\monitor-git-cursor.ps1 -Watch

# Con soglia personalizzata
.\monitor-git-cursor.ps1 -Threshold 20

# Con auto-kill quando supera la soglia
.\monitor-git-cursor.ps1 -Threshold 15 -AutoKill
```

**Caratteristiche:**
- Mostra processi Git raggruppati per processo padre (Cursor, VS, ecc.)
- Identifica quale applicazione genera più processi
- Avvisa quando si superano le soglie di sicurezza
- Modalità watch per monitoraggio continuo

### 2. `kill-git-blocked.ps1`
Termina automaticamente i processi Git bloccati o eccessivi.

**Uso:**
```powershell
# Termina processi che superano le soglie (con conferma)
.\kill-git-blocked.ps1

# Termina automaticamente senza conferma
.\kill-git-blocked.ps1 -Force

# Soglia personalizzata
.\kill-git-blocked.ps1 -Threshold 5 -Force

# Solo processi Git di Cursor
.\kill-git-blocked.ps1 -ParentProcess "Cursor" -Force

# Dry run (mostra cosa verrebbe fatto)
.\kill-git-blocked.ps1 -DryRun
```

**Parametri:**
- `-Threshold`: Numero massimo di processi Git consentiti (default: 10)
- `-MaxRuntimeMinutes`: Tempo massimo di esecuzione prima di terminare (default: 10)
- `-ParentProcess`: Filtra per processo padre specifico
- `-DryRun`: Mostra cosa verrebbe fatto senza terminare
- `-Force`: Termina senza chiedere conferma

### 3. `check-cursor-git-settings.ps1`
Verifica le impostazioni di Cursor relative a Git e suggerisce ottimizzazioni.

**Uso:**
```powershell
.\check-cursor-git-settings.ps1
```

**Caratteristiche:**
- Verifica se Cursor è in esecuzione
- Controlla le impostazioni Git di Cursor
- Suggerisce modifiche per prevenire problemi
- Mostra comandi utili per la gestione

### 4. `git_monitor.ps1` (esistente)
Monitor generale per processi Git e repository grandi.

**Uso:**
```powershell
.\git_monitor.ps1
```

## Workflow Consigliato

### Monitoraggio Preventivo
```powershell
# Monitoraggio continuo in background
.\monitor-git-cursor.ps1 -Watch
```

### Quando si Verifica il Problema
```powershell
# 1. Verifica situazione attuale
.\monitor-git-cursor.ps1

# 2. Se ci sono troppi processi, termina automaticamente
.\kill-git-blocked.ps1 -Threshold 15 -Force

# 3. Verifica impostazioni Cursor
.\check-cursor-git-settings.ps1
```

### Soluzione Rapida
```powershell
# Usa lo script principale nella root
..\..\fix-git-blocked.ps1 -Force
```

## Soglie Consigliate

- **Normale**: 0-10 processi Git
- **Warning**: 10-15 processi Git
- **Critico**: >15 processi Git (richiede intervento)

## Soluzioni Permanenti

1. **Aggiorna Cursor** all'ultima versione
2. **Modifica impostazioni Cursor**:
   - `git.autorefresh`: false
   - `git.autoRepositoryDetection`: false
   - Agents > Legacy Terminal Tool: prova ad attivare/disattivare
3. **Usa terminale esterno** per comandi Git complessi
4. **Riavvia Cursor** periodicamente se il problema persiste

## Documentazione Completa

Vedi `cursor-git-problem-solutions.md` nella root del progetto per:
- Dettagli sul problema
- Soluzioni complete
- Link ai forum di Cursor
- Troubleshooting avanzato

## Note

- Questi strumenti sono specifici per Windows PowerShell
- Richiedono privilegi per terminare processi
- Alcune funzionalità richiedono CIM (Get-CimInstance)
- I processi Git vengono rigenerati automaticamente da Cursor, quindi potrebbe essere necessario eseguire gli script periodicamente



