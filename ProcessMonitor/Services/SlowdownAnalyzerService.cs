using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class SlowdownAnalyzerService
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly OwnerResolver _ownerResolver;
    private readonly TagEnricher _tagEnricher;
    private readonly SystemHealthService _healthService;

    public SlowdownAnalyzerService(
        ProcessSnapshotService snapshotService,
        OwnerResolver ownerResolver,
        TagEnricher tagEnricher,
        SystemHealthService healthService)
    {
        _snapshotService = snapshotService;
        _ownerResolver = ownerResolver;
        _tagEnricher = tagEnricher;
        _healthService = healthService;
    }

    public SlowdownDiagnosis Diagnose(string? focus = null)
    {
        var snapshot = _snapshotService.CaptureSnapshot();
        _ownerResolver.AssignOwners(snapshot);
        _tagEnricher.ApplyTags(snapshot);

        var health = _healthService.CaptureHealthSnapshot(snapshot);
        var owners = health.Suspects;
        var reasons = BuildReasons(snapshot, health);
        var focused = ApplyFocus(snapshot, focus);

        return new SlowdownDiagnosis
        {
            CapturedAt = DateTime.Now,
            Health = health,
            Owners = owners,
            Reasons = reasons,
            FocusProcesses = focused.Take(15).Select(node => new TopProcessSample
            {
                ProcessId = node.ProcessId,
                ProcessName = node.ProcessName,
                OwnerId = node.OwnerId,
                OwnerPath = node.OwnerPath.ToList(),
                Tags = node.Tags.ToList(),
                LaunchCategory = node.LaunchCategory,
                CpuPercent = node.CpuUsage,
                MemoryMB = node.MemoryMB,
                ReadMBps = node.ReadBytesPerSecond / (1024.0 * 1024.0),
                WriteMBps = node.WriteBytesPerSecond / (1024.0 * 1024.0),
                ThreadCount = node.ThreadCount,
                HandleCount = node.HandleCount,
                Reason = BuildFocusReason(node)
            }).ToList(),
            Summary = BuildSummary(health, reasons)
        };
    }

    private static List<DiagnosisReason> BuildReasons(List<ProcessTreeNode> snapshot, SystemHealthSnapshot health)
    {
        var reasons = new List<DiagnosisReason>();
        var cpuTop = health.CpuTop.FirstOrDefault(sample => !ProcessMonitorClassifier.IsDiagnosticNoise(sample.ProcessName, string.Empty));
        var browserNodes = snapshot.Where(n => n.Tags.Contains("browser")).ToList();
        var securityNodes = snapshot.Where(n => n.Tags.Contains("security")).ToList();
        var gitNodes = snapshot.Where(n => n.Tags.Contains("git") && (n.Tags.Contains("cursor") || n.Tags.Contains("windsurf") || n.Tags.Contains("vscode-family") || n.OwnerPath.Contains("IDE"))).ToList();
        var tuningNodes = snapshot.Where(n => n.Tags.Contains("tuning")).ToList();

        if (cpuTop != null && health.TotalCpuPercent >= 50 && cpuTop.CpuPercent >= 15)
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "high_cpu_single_process",
                Summary = $"{cpuTop.ProcessName} sta consumando CPU in modo visibile",
                Severity = health.TotalCpuPercent >= 80 ? "critical" : "high",
                Confidence = "high",
                ProcessIds = [cpuTop.ProcessId],
                OwnerIds = cpuTop.OwnerId is null ? new List<string>() : [cpuTop.OwnerId],
                Evidence =
                [
                    $"CPU totale {health.TotalCpuPercent:F0}%",
                    $"Top CPU: {cpuTop.ProcessName} {cpuTop.CpuPercent:F1}%",
                    $"Owner: {string.Join(" > ", cpuTop.OwnerPath)}"
                ],
                SuggestedNextAction = $"Ispeziona il PID {cpuTop.ProcessId} e verifica se è lavoro utile o runaway."
            });
        }

        if (browserNodes.Count >= 4 && browserNodes.Sum(n => n.CpuUsage) >= 20)
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "multi_process_browser_load",
                Summary = "Un browser multi-processo sta contribuendo al carico",
                Severity = "medium",
                Confidence = "high",
                ProcessIds = browserNodes.OrderByDescending(n => n.CpuUsage).Take(8).Select(n => n.ProcessId).ToList(),
                OwnerIds = browserNodes.Select(n => n.OwnerId!).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(),
                Evidence =
                [
                    $"{browserNodes.Count} processi browser attivi",
                    $"CPU browser aggregata {browserNodes.Sum(n => n.CpuUsage):F1}%",
                    $"MEM browser aggregata {browserNodes.Sum(n => n.MemoryMB):F0} MB"
                ],
                SuggestedNextAction = "Chiudi solo tab/renderer inutili prima di toccare l'intera app browser."
            });
        }

        if (securityNodes.Any() && health.Pressure.PrimaryBottleneck is "Disk-write pressure" or "Mixed")
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "security_scan_write_pressure",
                Summary = "Tool di sicurezza stanno scrivendo o scandendo in background",
                Severity = "high",
                Confidence = "medium",
                ProcessIds = securityNodes.OrderByDescending(n => n.WriteBytesPerSecond).Take(6).Select(n => n.ProcessId).ToList(),
                OwnerIds = securityNodes.Select(n => n.OwnerId!).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(),
                Evidence =
                [
                    $"Processi security: {securityNodes.Count}",
                    $"Scritture security aggregate {(securityNodes.Sum(n => n.WriteBytesPerSecond) / (1024.0 * 1024.0)):F2} MB/s",
                    $"Disk busy {health.DiskBusyPercent:F0}%"
                ],
                SuggestedNextAction = "Non killare l'agent: verifica scansioni attive o esclusioni prima di intervenire."
            });
        }

        if (gitNodes.Count >= 3)
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "git_churn_under_ide",
                Summary = "L'IDE sta generando attività Git ricorsiva o ripetitiva",
                Severity = "medium",
                Confidence = "medium",
                ProcessIds = gitNodes.OrderByDescending(n => n.CpuUsage + ((n.ReadBytesPerSecond + n.WriteBytesPerSecond) / (1024.0 * 1024.0))).Take(8).Select(n => n.ProcessId).ToList(),
                OwnerIds = gitNodes.Select(n => n.OwnerId!).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(),
                Evidence =
                [
                    $"{gitNodes.Count} processi git/git-related sotto owner IDE",
                    $"Owner coinvolti: {string.Join(", ", gitNodes.Select(n => string.Join(" > ", n.OwnerPath)).Distinct().Take(3))}",
                    $"Git network attivi: {gitNodes.Count(n => n.Tags.Contains("git-network"))}"
                ],
                SuggestedNextAction = "Apri tree investigation sull'IDE focus e valuta solo il sottoalbero Git."
            });
        }

        if (health.PagesPerSec >= 200 && health.PageReadsPerSec >= 20 && health.AvailableMemoryMB >= 2048)
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "paging_without_ram_exhaustion",
                Summary = "C'è paging percepibile anche senza RAM completamente esaurita",
                Severity = "medium",
                Confidence = "medium",
                Evidence =
                [
                    $"Available memory {health.AvailableMemoryMB:F0} MB",
                    $"Pages/sec {health.PagesPerSec:F0}",
                    $"Page reads/sec {health.PageReadsPerSec:F0}"
                ],
                SuggestedNextAction = "Cerca processi con working set ampio e I/O di background prima di imputare tutto alla RAM."
            });
        }

        if (tuningNodes.Any() && tuningNodes.Sum(n => n.CpuUsage) >= 10)
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "tuning_tool_pressure",
                Summary = "Tool di tuning/governance stanno contribuendo al carico",
                Severity = "medium",
                Confidence = "high",
                ProcessIds = tuningNodes.Select(n => n.ProcessId).ToList(),
                OwnerIds = tuningNodes.Select(n => n.OwnerId!).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList(),
                Evidence =
                [
                    $"CPU tuning aggregata {tuningNodes.Sum(n => n.CpuUsage):F1}%",
                    $"Processi: {string.Join(", ", tuningNodes.Select(n => n.ProcessName).Distinct())}"
                ],
                SuggestedNextAction = "Valuta un test breve senza ProcessLasso/ProcessGovernor per misurare il delta reale."
            });
        }

        if (!reasons.Any())
        {
            reasons.Add(new DiagnosisReason
            {
                Code = "no_dominant_reason",
                Summary = "Nessun colpevole dominante, probabile carico distribuito o intermittente",
                Severity = "low",
                Confidence = "low",
                SuggestedNextAction = "Salva uno snapshot e confrontalo con uno stato buono per isolare il delta."
            });
        }

        return reasons
            .OrderByDescending(reason => SeverityRank(reason.Severity))
            .ThenByDescending(reason => ConfidenceRank(reason.Confidence))
            .ToList();
    }

    public List<ProcessTreeNode> ApplyFocus(List<ProcessTreeNode> snapshot, string? focus)
    {
        if (string.IsNullOrWhiteSpace(focus))
        {
            return snapshot
                .OrderByDescending(n => n.CpuUsage)
                .ThenByDescending(n => n.ReadBytesPerSecond + n.WriteBytesPerSecond)
                .ThenByDescending(n => n.MemoryMB)
                .Take(15)
                .ToList();
        }

        var filter = focus.Trim().ToLowerInvariant();

        if (filter is "top cpu" or "top-cpu")
            return snapshot.OrderByDescending(n => n.CpuUsage).Take(25).ToList();
        if (filter is "top io" or "top-io")
            return snapshot.OrderByDescending(n => n.ReadBytesPerSecond + n.WriteBytesPerSecond).Take(25).ToList();
        if (filter is "top mem" or "top memory" or "top-mem")
            return snapshot.OrderByDescending(n => n.MemoryMB).Take(25).ToList();

        return snapshot
            .Where(node =>
                node.OwnerPath.Any(part => part.Equals(filter, StringComparison.OrdinalIgnoreCase)) ||
                node.OwnerId?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true ||
                node.Tags.Any(tag => tag.Equals(filter, StringComparison.OrdinalIgnoreCase)) ||
                node.LaunchCategory.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                (filter.StartsWith("owner:") && node.OwnerId?.Contains(filter["owner:".Length..], StringComparison.OrdinalIgnoreCase) == true) ||
                (filter.StartsWith("tag:") && node.Tags.Any(tag => tag.Equals(filter["tag:".Length..], StringComparison.OrdinalIgnoreCase))) ||
                (filter.StartsWith("kind:") && node.LaunchCategory.Equals(filter["kind:".Length..], StringComparison.OrdinalIgnoreCase)) ||
                (filter.StartsWith("pressure:disk") && (node.ReadBytesPerSecond + node.WriteBytesPerSecond) > 512 * 1024) ||
                (filter.StartsWith("pressure:cpu") && node.CpuUsage >= 5) ||
                node.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.CpuUsage)
            .ThenByDescending(n => n.ReadBytesPerSecond + n.WriteBytesPerSecond)
            .ThenByDescending(n => n.MemoryMB)
            .Take(25)
            .ToList();
    }

    private static string BuildSummary(SystemHealthSnapshot health, List<DiagnosisReason> reasons)
    {
        var lead = reasons.FirstOrDefault()?.Summary ?? "Nessuna diagnosi primaria";
        return $"{health.Pressure.PrimaryBottleneck}: {lead}";
    }

    private static string BuildFocusReason(ProcessTreeNode node)
    {
        if (node.CpuUsage >= 5)
            return $"cpu {node.CpuUsage:F1}%";
        if ((node.ReadBytesPerSecond + node.WriteBytesPerSecond) > 512 * 1024)
            return $"io {(node.ReadBytesPerSecond + node.WriteBytesPerSecond) / (1024.0 * 1024.0):F2} MB/s";
        if (node.MemoryMB >= 256)
            return $"mem {node.MemoryMB:F0} MB";
        return node.LaunchCategory.ToLowerInvariant();
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        _ => 1
    };

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };
}
