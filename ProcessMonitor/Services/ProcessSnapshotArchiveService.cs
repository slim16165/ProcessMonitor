using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ProcessSnapshotArchiveService
{
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
            .GroupBy(BuildSignature)
            .ToDictionary(
                g => g.Key,
                g => new SnapshotSignatureInfo
                {
                    Process = g.First(),
                    Count = g.Count()
                });
    }

    private static string BuildSignature(ProcessSnapshotEntry process)
    {
        var normalizedCommand = process.CommandLine.Length > 160
            ? process.CommandLine[..160]
            : process.CommandLine;
        return $"{process.OwnerId}|{process.ProcessName}|{normalizedCommand}";
    }

    private sealed class SnapshotSignatureInfo
    {
        public required ProcessSnapshotEntry Process { get; init; }
        public required int Count { get; init; }
    }
}
