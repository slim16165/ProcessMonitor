using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class SlowdownPlannerService
{
    public SlowdownPlan BuildPlan(SlowdownDiagnosis diagnosis)
    {
        var actions = new List<SuggestedAction>();

        foreach (var reason in diagnosis.Reasons)
        {
            switch (reason.Code)
            {
                case "high_cpu_single_process":
                    actions.Add(new SuggestedAction
                    {
                        Type = "observe",
                        Severity = reason.Severity,
                        Confidence = reason.Confidence,
                        Summary = "Conferma se il top CPU sta facendo lavoro utile",
                        Detail = reason.SuggestedNextAction,
                        ProcessIds = reason.ProcessIds
                    });
                    actions.Add(new SuggestedAction
                    {
                        Type = "open-tool",
                        Severity = "medium",
                        Confidence = "high",
                        Summary = "Apri Process Explorer sul processo più caldo",
                        Detail = "Usa Process Explorer o inspect tree sul PID top CPU.",
                        ProcessIds = reason.ProcessIds,
                        ToolHint = "Process Explorer"
                    });
                    break;
                case "multi_process_browser_load":
                    actions.Add(new SuggestedAction
                    {
                        Type = "kill-leaf",
                        Severity = "medium",
                        Confidence = "medium",
                        Summary = "Chiudi solo renderer/tab browser inutili",
                        Detail = "Evita di chiudere tutto il browser se bastano pochi renderer o tab pesanti.",
                        ProcessIds = reason.ProcessIds
                    });
                    break;
                case "security_scan_write_pressure":
                    actions.Add(new SuggestedAction
                    {
                        Type = "observe",
                        Severity = "high",
                        Confidence = "high",
                        Summary = "Non toccare l'agent di sicurezza",
                        Detail = "Verifica scansioni, log o esclusioni; non killare Sentinel/Forti/Defender come prima risposta.",
                        ProcessIds = reason.ProcessIds
                    });
                    break;
                case "git_churn_under_ide":
                    actions.Add(new SuggestedAction
                    {
                        Type = "kill-subtree",
                        Severity = "medium",
                        Confidence = "medium",
                        Summary = "Valuta solo il sottoalbero Git dell'IDE",
                        Detail = "Ispeziona il tree dell'IDE e colpisci solo leaf Git/console senza TCP attivo.",
                        ProcessIds = reason.ProcessIds
                    });
                    break;
                case "paging_without_ram_exhaustion":
                    actions.Add(new SuggestedAction
                    {
                        Type = "recheck",
                        Severity = "medium",
                        Confidence = "medium",
                        Summary = "Rimisura dopo aver chiuso i processi con footprint alto ma non essenziali",
                        Detail = "Il problema non è RAM esaurita pura: cerca chi genera paging o flush.",
                        ProcessIds = reason.ProcessIds
                    });
                    break;
                case "tuning_tool_pressure":
                    actions.Add(new SuggestedAction
                    {
                        Type = "observe",
                        Severity = "medium",
                        Confidence = "high",
                        Summary = "ProcessLasso/ProcessGovernor probabili contributori",
                        Detail = "Fai un test breve con il tuning tool fermo e confronta health/why-slow prima-dopo.",
                        ProcessIds = reason.ProcessIds
                    });
                    break;
            }
        }

        var gitCache = diagnosis.FocusProcesses.FirstOrDefault(p => p.Tags.Contains("git-cache"));
        if (gitCache != null)
        {
            actions.Add(new SuggestedAction
            {
                Type = "kill-leaf",
                Severity = "low",
                Confidence = "medium",
                Summary = "Ferma TGitCache se non ti serve ora",
                Detail = "Può essere un test rapido e reversibile quando Git cache sta ronzando in background.",
                ProcessIds = [gitCache.ProcessId]
            });
        }

        if (!actions.Any())
        {
            actions.Add(new SuggestedAction
            {
                Type = "observe",
                Severity = "low",
                Confidence = "low",
                Summary = "Nessuna azione forte suggerita",
                Detail = "Salva uno snapshot e usa snapshot-diff-current o snapshot-diff-health per capire il delta."
            });
        }

        return new SlowdownPlan
        {
            Summary = diagnosis.Summary,
            Severity = diagnosis.Reasons.FirstOrDefault()?.Severity ?? "medium",
            Confidence = diagnosis.Reasons.FirstOrDefault()?.Confidence ?? "medium",
            Reasons = diagnosis.Reasons.Select(reason => $"{reason.Code}: {reason.Summary}").ToList(),
            Actions = actions
        };
    }
}
