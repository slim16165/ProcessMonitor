using System.Text.RegularExpressions;
using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessSnapshotArchiveService
{
    private static readonly Regex TokenRegex = new("\"[^\"]*\"|\\S+", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"\b(?:https?|ssh)://\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HashRegex = new(@"\b[0-9a-f]{7,40}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GuidRegex = new(@"\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled);

    private readonly ProcessSnapshotService _snapshotService;
    private readonly OwnerResolver _ownerResolver;
    private readonly TagEnricher _tagEnricher;
    private readonly string _snapshotDirectory;

    public ProcessSnapshotArchiveService(
        ProcessSnapshotService snapshotService,
        OwnerResolver ownerResolver,
        TagEnricher tagEnricher,
        string? snapshotDirectory = null)
    {
        _snapshotService = snapshotService;
        _ownerResolver = ownerResolver;
        _tagEnricher = tagEnricher;
        _snapshotDirectory = snapshotDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "snapshots");
    }

    public ProcessSystemSnapshot SaveSnapshot(string note)
    {
        var snapshot = CaptureSnapshot(note);
        Directory.CreateDirectory(_snapshotDirectory);

        var path = GetSnapshotPath(snapshot.SnapshotId);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        return snapshot;
    }

    public List<string> ListSnapshots()
    {
        if (!Directory.Exists(_snapshotDirectory))
            return new List<string>();

        return Directory.GetFiles(_snapshotDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(x => x)
            .ToList();
    }

    public ProcessSnapshotDiff DiffSnapshots(string baselineId, string currentId)
    {
        var baseline = LoadSnapshot(baselineId);
        var current = LoadSnapshot(currentId);
        return BuildDiff(baseline, current);
    }

    public ProcessSnapshotDiff DiffSnapshotAgainstCurrent(string baselineId, string currentNote = "current-live")
    {
        var baseline = LoadSnapshot(baselineId);
        var current = CaptureSnapshot(currentNote);
        return BuildDiff(baseline, current);
    }

    public ProcessSnapshotDiff DiffLatestSnapshotAgainstCurrent(string currentNote = "current-live")
    {
        var latestSnapshotId = GetLatestSnapshotId()
            ?? throw new InvalidOperationException("Nessuno snapshot disponibile.");

        return DiffSnapshotAgainstCurrent(latestSnapshotId, currentNote);
    }

    public string? GetLatestSnapshotId()
    {
        return ListSnapshots().FirstOrDefault();
    }

    private static ProcessSnapshotDiff BuildDiff(ProcessSystemSnapshot baseline, ProcessSystemSnapshot current)
    {
        var baselineByOwner = baseline.Processes.GroupBy(p => p.OwnerId ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.ToList());
        var currentByOwner = current.Processes.GroupBy(p => p.OwnerId ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.ToList());

        var ownerKeys = baselineByOwner.Keys.Union(currentByOwner.Keys).OrderBy(x => x);
        var ownerDeltas = ownerKeys
            .Select(key => new OwnerDelta
            {
                OwnerId = key,
                OwnerPath = baselineByOwner.TryGetValue(key, out var baselineOwner)
                    ? baselineOwner.First().OwnerPath
                    : currentByOwner[key].First().OwnerPath,
                BaselineCount = baselineByOwner.TryGetValue(key, out var baselineList) ? baselineList.Count : 0,
                CurrentCount = currentByOwner.TryGetValue(key, out var currentList) ? currentList.Count : 0
            })
            .Where(delta => delta.DeltaCount != 0)
            .OrderByDescending(delta => Math.Abs(delta.DeltaCount))
            .ToList();

        var baselineSignatures = BuildSignatureMap(baseline.Processes);
        var currentSignatures = BuildSignatureMap(current.Processes);
        var allSignatures = baselineSignatures.Keys.Union(currentSignatures.Keys);

        var newSignatures = new List<ProcessSignatureDelta>();
        var removedSignatures = new List<ProcessSignatureDelta>();

        foreach (var signature in allSignatures)
        {
            var baselineCount = baselineSignatures.TryGetValue(signature, out var baselineEntry) ? baselineEntry.Count : 0;
            var currentCount = currentSignatures.TryGetValue(signature, out var currentEntry) ? currentEntry.Count : 0;
            var delta = currentCount - baselineCount;

            if (delta > 0)
            {
                newSignatures.Add(new ProcessSignatureDelta
                {
                    Signature = signature,
                    SignatureDetail = currentEntry!.SignatureDetail,
                    ProcessName = currentEntry!.Process.ProcessName,
                    OwnerId = currentEntry.Process.OwnerId,
                    BaselineCount = baselineCount,
                    CurrentCount = currentCount
                });
            }
            else if (delta < 0)
            {
                removedSignatures.Add(new ProcessSignatureDelta
                {
                    Signature = signature,
                    SignatureDetail = baselineEntry!.SignatureDetail,
                    ProcessName = baselineEntry!.Process.ProcessName,
                    OwnerId = baselineEntry.Process.OwnerId,
                    BaselineCount = baselineCount,
                    CurrentCount = currentCount
                });
            }
        }

        return new ProcessSnapshotDiff
        {
            BaselineId = baseline.SnapshotId,
            CurrentId = current.SnapshotId,
            BaselineCount = baseline.Processes.Count,
            CurrentCount = current.Processes.Count,
            OwnerDeltas = ownerDeltas,
            NewSignatures = newSignatures.OrderByDescending(x => x.DeltaCount).Take(25).ToList(),
            RemovedSignatures = removedSignatures.OrderBy(x => x.DeltaCount).Take(25).ToList()
        };
    }

    private ProcessSystemSnapshot CaptureSnapshot(string note)
    {
        var processes = _snapshotService.CaptureSnapshot();
        _ownerResolver.AssignOwners(processes);
        _tagEnricher.ApplyTags(processes);

        return new ProcessSystemSnapshot
        {
            SnapshotId = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            Note = note,
            CapturedAt = DateTime.Now,
            Processes = processes.Select(p => new ProcessSnapshotEntry
            {
                ProcessId = p.ProcessId,
                ParentProcessId = p.ParentProcessId,
                ProcessName = p.ProcessName,
                CommandLine = p.CommandLine,
                OwnerId = p.OwnerId,
                OwnerPath = p.OwnerPath.ToList(),
                Tags = p.Tags.ToList(),
                CpuUsage = p.CpuUsage,
                MemoryMB = p.MemoryMB,
                HasTcpActivity = p.HasTcpActivity,
                StartTime = p.StartTime
            }).ToList()
        };
    }

    private ProcessSystemSnapshot LoadSnapshot(string snapshotId)
    {
        var path = GetSnapshotPath(snapshotId);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProcessSystemSnapshot>(json)
            ?? throw new InvalidOperationException($"Snapshot non valido: {snapshotId}");
    }

    private string GetSnapshotPath(string snapshotId)
    {
        return Path.Combine(_snapshotDirectory, $"{snapshotId}.json");
    }

    private static Dictionary<string, SnapshotSignatureInfo> BuildSignatureMap(IEnumerable<ProcessSnapshotEntry> processes)
    {
        return processes
            .GroupBy(process => BuildSignature(process, out var _))
            .ToDictionary(
                g => g.Key,
                g => new SnapshotSignatureInfo
                {
                    Process = g.First(),
                    SignatureDetail = BuildSignatureDetail(g.First()),
                    Count = g.Count()
                });
    }

    private static string BuildSignature(ProcessSnapshotEntry process, out string signatureDetail)
    {
        signatureDetail = BuildSignatureDetail(process);
        return $"{process.OwnerId ?? "Unknown"}|{process.ProcessName}|{signatureDetail}";
    }

    private static string BuildSignatureDetail(ProcessSnapshotEntry process)
    {
        var tokens = TokenRegex.Matches(process.CommandLine)
            .Select(match => match.Value.Trim().Trim('"'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (tokens.Count == 0)
        {
            return "<no-command>";
        }

        var normalizedProcessName = Path.GetFileNameWithoutExtension(process.ProcessName).ToLowerInvariant();

        return normalizedProcessName switch
        {
            "git" => BuildGitSignature(tokens),
            "git-remote-https" => "git-remote-https network",
            "git-credential-manager" => "git-credential-manager credential",
            "rg" => BuildToolSignature("rg", tokens, 6),
            "pwsh" or "powershell" or "cmd" => BuildShellSignature(normalizedProcessName, tokens),
            "node" => BuildToolSignature("node", tokens, 6),
            "python" => BuildToolSignature("python", tokens, 6),
            _ => BuildToolSignature(normalizedProcessName, tokens, 6)
        };
    }

    private static string BuildGitSignature(IReadOnlyList<string> tokens)
    {
        var gitIndex = FindTokenIndex(tokens, "git");
        var relevantTokens = gitIndex >= 0
            ? tokens.Skip(gitIndex + 1)
            : tokens;

        var normalized = relevantTokens
            .Select(NormalizeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Take(8)
            .ToList();

        if (normalized.Count == 0)
        {
            return "git";
        }

        return $"git {string.Join(' ', normalized)}";
    }

    private static string BuildShellSignature(string shellName, IReadOnlyList<string> tokens)
    {
        var commandText = string.Join(' ', tokens).ToLowerInvariant();

        if (commandText.Contains("git "))
        {
            return $"{shellName} -> {BuildGitSignature(tokens)}";
        }

        if (commandText.Contains("rg --files"))
        {
            return $"{shellName} -> rg --files";
        }

        if (commandText.Contains("npx "))
        {
            return $"{shellName} -> {BuildToolSignature("npx", tokens, 6)}";
        }

        if (commandText.Contains("uvx ") || commandText.Contains(" uv "))
        {
            return $"{shellName} -> {BuildToolSignature("uv", tokens, 6)}";
        }

        return BuildToolSignature(shellName, tokens, 6);
    }

    private static string BuildToolSignature(string toolName, IReadOnlyList<string> tokens, int maxTokens)
    {
        var startIndex = FindTokenIndex(tokens, toolName);
        var relevantTokens = startIndex >= 0
            ? tokens.Skip(startIndex + 1)
            : tokens;

        var normalized = relevantTokens
            .Select(NormalizeToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Take(maxTokens)
            .ToList();

        if (normalized.Count == 0)
        {
            return toolName;
        }

        return $"{toolName} {string.Join(' ', normalized)}";
    }

    private static int FindTokenIndex(IReadOnlyList<string> tokens, string toolName)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var tokenName = Path.GetFileNameWithoutExtension(tokens[index]).ToLowerInvariant();
            if (tokenName == toolName)
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var trimmed = token.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (UrlRegex.IsMatch(trimmed))
        {
            return "<url>";
        }

        if (LooksLikePath(trimmed))
        {
            return "<path>";
        }

        if (GuidRegex.IsMatch(trimmed))
        {
            return "<guid>";
        }

        if (HashRegex.IsMatch(trimmed))
        {
            return "<hash>";
        }

        if (NumberRegex.IsMatch(trimmed))
        {
            return NumberRegex.Replace(trimmed, "<n>");
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(":\\", StringComparison.Ordinal)
            || value.StartsWith("\\\\", StringComparison.Ordinal)
            || value.Contains("\\", StringComparison.Ordinal)
            || value.Contains("/", StringComparison.Ordinal);
    }

    private sealed class SnapshotSignatureInfo
    {
        public required ProcessSnapshotEntry Process { get; init; }
        public required string SignatureDetail { get; init; }
        public required int Count { get; init; }
    }
}
