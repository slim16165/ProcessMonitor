using System.Management;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class SystemHealthService
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly OwnerResolver _ownerResolver;
    private readonly TagEnricher _tagEnricher;

    public SystemHealthService(
        ProcessSnapshotService snapshotService,
        OwnerResolver ownerResolver,
        TagEnricher tagEnricher)
    {
        _snapshotService = snapshotService;
        _ownerResolver = ownerResolver;
        _tagEnricher = tagEnricher;
    }

    public SystemHealthSnapshot CaptureHealthSnapshot(List<ProcessTreeNode>? existingSnapshot = null)
    {
        var nodes = existingSnapshot ?? _snapshotService.CaptureSnapshot();
        _ownerResolver.AssignOwners(nodes);
        _tagEnricher.ApplyTags(nodes);

        var system = GetSystemPerformance();
        var uptime = GetMachineUptime();

        var health = new SystemHealthSnapshot
        {
            CapturedAt = DateTime.Now,
            MachineUptime = uptime,
            TotalCpuPercent = system.TotalCpuPercent,
            UserCpuPercent = system.UserCpuPercent,
            KernelCpuPercent = system.KernelCpuPercent,
            InterruptCpuPercent = system.InterruptCpuPercent,
            DpcCpuPercent = system.DpcCpuPercent,
            DiskBusyPercent = system.DiskBusyPercent,
            DiskQueueLength = system.DiskQueueLength,
            DiskBytesPerSec = system.DiskBytesPerSec,
            DiskReadBytesPerSec = system.DiskReadBytesPerSec,
            DiskWriteBytesPerSec = system.DiskWriteBytesPerSec,
            AvailableMemoryMB = system.AvailableMemoryMB,
            CommittedMemoryMB = system.CommittedMemoryMB,
            CommitLimitMB = system.CommitLimitMB,
            PagesPerSec = system.PagesPerSec,
            PageReadsPerSec = system.PageReadsPerSec,
            PageFileUsageMB = system.PageFileUsageMB,
            PageFilePeakUsageMB = system.PageFilePeakUsageMB,
            PageFileAllocatedMB = system.PageFileAllocatedMB,
            CpuTop = BuildTopProcessList(nodes.Where(IsRelevantProcess).OrderByDescending(n => n.CpuUsage).Take(10), "cpu").ToList(),
            IoTop = BuildTopProcessList(
                nodes.Where(IsRelevantProcess).OrderByDescending(n => n.ReadBytesPerSecond + n.WriteBytesPerSecond).Take(10),
                "io").ToList(),
            MemoryTop = BuildTopProcessList(nodes.Where(IsRelevantProcess).OrderByDescending(n => n.MemoryMB).Take(10), "memory").ToList(),
            Suspects = BuildOwnerPressureSummary(nodes.Where(IsRelevantProcess)).Take(10).ToList()
        };

        health.Pressure = AssessPressure(health);
        return health;
    }

    private static IEnumerable<TopProcessSample> BuildTopProcessList(IEnumerable<ProcessTreeNode> nodes, string mode)
    {
        foreach (var node in nodes)
        {
            yield return new TopProcessSample
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
                Reason = BuildReason(node, mode)
            };
        }
    }

    private static string BuildReason(ProcessTreeNode node, string mode)
    {
        if (mode == "cpu")
            return $"CPU {node.CpuUsage:F1}%";
        if (mode == "io")
            return $"I/O {(node.ReadBytesPerSecond + node.WriteBytesPerSecond) / (1024.0 * 1024.0):F2} MB/s";
        return $"MEM {node.MemoryMB:F1} MB";
    }

    private static IEnumerable<OwnerPressureSummary> BuildOwnerPressureSummary(IEnumerable<ProcessTreeNode> nodes)
    {
        return nodes
            .GroupBy(n => n.OwnerId ?? "Unknown")
            .Select(group =>
            {
                var tags = group.SelectMany(n => n.Tags).Distinct().OrderBy(x => x).ToList();
                var cpu = group.Sum(n => n.CpuUsage);
                var memory = group.Sum(n => n.MemoryMB);
                var readMb = group.Sum(n => n.ReadBytesPerSecond) / (1024.0 * 1024.0);
                var writeMb = group.Sum(n => n.WriteBytesPerSecond) / (1024.0 * 1024.0);
                return new OwnerPressureSummary
                {
                    OwnerId = group.Key,
                    OwnerPath = group.First().OwnerPath,
                    ProcessCount = group.Count(),
                    CpuPercent = cpu,
                    MemoryMB = memory,
                    ReadMBps = readMb,
                    WriteMBps = writeMb,
                    Tags = tags,
                    DominantReason = BuildOwnerReason(cpu, memory, readMb + writeMb, tags)
                };
            })
            .OrderByDescending(summary => summary.CpuPercent + summary.WriteMBps + summary.ReadMBps)
            .ThenByDescending(summary => summary.MemoryMB);
    }

    private static string BuildOwnerReason(double cpu, double memory, double ioMb, List<string> tags)
    {
        if (tags.Contains("security"))
            return "security/background monitoring";
        if (tags.Contains("tuning"))
            return "tuning/governor overhead";
        if (tags.Contains("browser"))
            return "multi-process browser load";
        if (tags.Contains("git"))
            return "git activity";
        if (ioMb >= 5)
            return "disk write pressure";
        if (cpu >= 20)
            return "cpu pressure";
        if (memory >= 1024)
            return "memory footprint";
        return "background activity";
    }

    private static PressureAssessment AssessPressure(SystemHealthSnapshot health)
    {
        var cpuScore = Math.Min(100, health.TotalCpuPercent);
        var diskScore = Math.Min(100, health.DiskBusyPercent + (health.DiskQueueLength * 8) + ((health.DiskWriteBytesPerSec / (1024 * 1024)) * 2));
        var memoryScore = Math.Min(100,
            (health.PagesPerSec * 0.15) +
            (health.PageReadsPerSec * 1.5) +
            (health.AvailableMemoryMB < 4096 ? 30 : 0) +
            (health.PageFileUsageMB > 8192 ? 20 : 0));

        var ordered = new List<(string Name, double Score)>
        {
            ("CPU-bound", cpuScore),
            ("Disk-write pressure", diskScore),
            ("Memory pressure / paging", memoryScore)
        }
        .OrderByDescending(item => item.Score)
        .ToList();

        var primary = ordered[0];
        var secondary = ordered[1];
        var mixed = primary.Score >= 45 && secondary.Score >= 40 && Math.Abs(primary.Score - secondary.Score) <= 15;

        return new PressureAssessment
        {
            PrimaryBottleneck = mixed
                ? "Mixed"
                : primary.Score < 30
                    ? "No obvious bottleneck"
                    : primary.Name,
            SecondaryBottleneck = mixed ? secondary.Name : secondary.Score >= 35 ? secondary.Name : null,
            Summary = BuildPressureSummary(primary, secondary, mixed, health),
            CpuScore = Math.Round(cpuScore, 1),
            DiskScore = Math.Round(diskScore, 1),
            MemoryScore = Math.Round(memoryScore, 1)
        };
    }

    private static string BuildPressureSummary((string Name, double Score) primary, (string Name, double Score) secondary, bool mixed, SystemHealthSnapshot health)
    {
        if (primary.Score < 30)
            return "Nessuna pressione evidente: controlla processi intermittenti o latenze esterne.";
        if (mixed)
            return $"Pressione mista: {primary.Name} e {secondary.Name} stanno contribuendo insieme al rallentamento.";
        if (primary.Name == "CPU-bound")
            return $"CPU alta ({health.TotalCpuPercent:F0}%) con processi attivi in foreground/background.";
        if (primary.Name == "Disk-write pressure")
            return $"Scritture disco elevate ({health.DiskWriteBytesPerSec / (1024 * 1024):F1} MB/s) con possibile lavoro di servizi o browser.";
        return $"Paging e page reads elevati ({health.PageReadsPerSec:F0}/s) stanno degradando la reattività.";
    }

    private static TimeSpan GetMachineUptime()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["LastBootUpTime"]?.ToString() is { } bootTimeRaw)
                {
                    var bootTime = ManagementDateTimeConverter.ToDateTime(bootTimeRaw);
                    return DateTime.Now - bootTime;
                }
            }
        }
        catch
        {
        }

        return TimeSpan.Zero;
    }

    private static SystemCounters GetSystemPerformance()
    {
        var result = new SystemCounters();

        try
        {
            using var cpuSearcher = new ManagementObjectSearcher(
                "SELECT Name, PercentProcessorTime, PercentUserTime, PercentPrivilegedTime FROM Win32_PerfFormattedData_PerfOS_Processor");
            foreach (ManagementObject obj in cpuSearcher.Get())
            {
                if (!string.Equals(obj["Name"]?.ToString(), "_Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.TotalCpuPercent = ReadDouble(obj, "PercentProcessorTime");
                result.UserCpuPercent = ReadDouble(obj, "PercentUserTime");
                result.KernelCpuPercent = ReadDouble(obj, "PercentPrivilegedTime");
                result.InterruptCpuPercent = ReadDouble(obj, "PercentInterruptTime");
                result.DpcCpuPercent = ReadDouble(obj, "PercentDPCTime");
                break;
            }
        }
        catch
        {
        }

        try
        {
            using var interruptSearcher = new ManagementObjectSearcher(
                "SELECT Name, PercentInterruptTime, PercentDPCTime FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
            foreach (ManagementObject obj in interruptSearcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "0,_Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.InterruptCpuPercent = ReadDouble(obj, "PercentInterruptTime");
                result.DpcCpuPercent = ReadDouble(obj, "PercentDPCTime");
                break;
            }
        }
        catch
        {
        }

        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT Name, PercentDiskTime, AvgDiskQueueLength, DiskBytesPersec, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            foreach (ManagementObject obj in diskSearcher.Get())
            {
                if (!string.Equals(obj["Name"]?.ToString(), "_Total", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.DiskBusyPercent = ReadDouble(obj, "PercentDiskTime");
                result.DiskQueueLength = ReadDouble(obj, "AvgDiskQueueLength");
                result.DiskBytesPerSec = ReadDouble(obj, "DiskBytesPersec");
                result.DiskReadBytesPerSec = ReadDouble(obj, "DiskReadBytesPersec");
                result.DiskWriteBytesPerSec = ReadDouble(obj, "DiskWriteBytesPersec");
                break;
            }
        }
        catch
        {
        }

        try
        {
            using var memorySearcher = new ManagementObjectSearcher(
                "SELECT AvailableMBytes, PagesPersec, PageReadsPersec, CommittedBytes, CommitLimit FROM Win32_PerfFormattedData_PerfOS_Memory");
            foreach (ManagementObject obj in memorySearcher.Get())
            {
                result.AvailableMemoryMB = ReadDouble(obj, "AvailableMBytes");
                result.PagesPerSec = ReadDouble(obj, "PagesPersec");
                result.PageReadsPerSec = ReadDouble(obj, "PageReadsPersec");
                result.CommittedMemoryMB = ReadDouble(obj, "CommittedBytes") / (1024 * 1024);
                result.CommitLimitMB = ReadDouble(obj, "CommitLimit") / (1024 * 1024);
                break;
            }
        }
        catch
        {
        }

        try
        {
            using var pageSearcher = new ManagementObjectSearcher(
                "SELECT CurrentUsage, PeakUsage, AllocatedBaseSize FROM Win32_PageFileUsage");
            foreach (ManagementObject obj in pageSearcher.Get())
            {
                result.PageFileUsageMB += ReadDouble(obj, "CurrentUsage");
                result.PageFilePeakUsageMB += ReadDouble(obj, "PeakUsage");
                result.PageFileAllocatedMB += ReadDouble(obj, "AllocatedBaseSize");
            }
        }
        catch
        {
        }

        return result;
    }

    private static double ReadDouble(ManagementObject obj, string propertyName)
    {
        try
        {
            return obj[propertyName] != null ? Convert.ToDouble(obj[propertyName]) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class SystemCounters
    {
        public double TotalCpuPercent { get; set; }
        public double UserCpuPercent { get; set; }
        public double KernelCpuPercent { get; set; }
        public double InterruptCpuPercent { get; set; }
        public double DpcCpuPercent { get; set; }
        public double DiskBusyPercent { get; set; }
        public double DiskQueueLength { get; set; }
        public double DiskBytesPerSec { get; set; }
        public double DiskReadBytesPerSec { get; set; }
        public double DiskWriteBytesPerSec { get; set; }
        public double AvailableMemoryMB { get; set; }
        public double CommittedMemoryMB { get; set; }
        public double CommitLimitMB { get; set; }
        public double PagesPerSec { get; set; }
        public double PageReadsPerSec { get; set; }
        public double PageFileUsageMB { get; set; }
        public double PageFilePeakUsageMB { get; set; }
        public double PageFileAllocatedMB { get; set; }
    }

    private static bool IsRelevantProcess(ProcessTreeNode node)
    {
        return node.ProcessId > 0 &&
               !string.Equals(node.ProcessName, "System Idle Process", StringComparison.OrdinalIgnoreCase) &&
               !ProcessMonitorClassifier.IsDiagnosticNoise(node.ProcessName, node.CommandLine);
    }
}
