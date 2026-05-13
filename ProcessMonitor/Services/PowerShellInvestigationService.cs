using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProcessMonitor.Services;

public class PowerShellInvestigationService
{
    private readonly ILogger<PowerShellInvestigationService>? _logger;

    public PowerShellInvestigationService(ILogger<PowerShellInvestigationService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ProcessInvestigationResult?> GetProcessDetails(int processId)
    {
        try
        {
            var script = $"Get-Process -Id {processId} | Select-Object Id, ProcessName, CPU, WorkingSet, PrivateMemory, HandleCount, ThreadCount, StartTime, Path | ConvertTo-Json";
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output))
                return null;

            var jsonDoc = JsonDocument.Parse(output);
            var root = jsonDoc.RootElement;

            return new ProcessInvestigationResult
            {
                ProcessId = root.TryGetProperty("Id", out var idProp) ? idProp.GetInt32() : processId,
                ProcessName = root.TryGetProperty("ProcessName", out var nameProp) ? nameProp.GetString() : "Unknown",
                CpuTime = root.TryGetProperty("CPU", out var cpuProp) ? cpuProp.GetString() : "N/A",
                WorkingSet = root.TryGetProperty("WorkingSet", out var wsProp) ? wsProp.GetInt64() : 0,
                PrivateMemory = root.TryGetProperty("PrivateMemory", out var pmProp) ? pmProp.GetInt64() : 0,
                HandleCount = root.TryGetProperty("HandleCount", out var hcProp) ? hcProp.GetInt32() : 0,
                ThreadCount = root.TryGetProperty("ThreadCount", out var tcProp) ? tcProp.GetInt32() : 0,
                StartTime = root.TryGetProperty("StartTime", out var stProp) ? stProp.GetDateTime() : DateTime.MinValue,
                Path = root.TryGetProperty("Path", out var pathProp) ? pathProp.GetString() : "N/A"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero dettagli processo {ProcessId}", processId);
            return null;
        }
    }

    public async Task<List<ProcessHandleInfo>> GetProcessHandles(int processId)
    {
        try
        {
            var script = $"(Get-Process -Id {processId}).Handles";
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output) || !int.TryParse(output.Trim(), out var handleCount))
                return new List<ProcessHandleInfo>();

            return new List<ProcessHandleInfo>
            {
                new ProcessHandleInfo
                {
                    HandleCount = handleCount,
                    ProcessId = processId,
                    Timestamp = DateTime.Now
                }
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero handles processo {ProcessId}", processId);
            return new List<ProcessHandleInfo>();
        }
    }

    public async Task<List<ProcessThreadInfo>> GetProcessThreads(int processId)
    {
        try
        {
            var script = $"(Get-Process -Id {processId}).Threads | Select-Object Id, TotalProcessorTime, UserProcessorTime, PrivilegedProcessorTime, State, WaitReason | ConvertTo-Json";
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output))
                return new List<ProcessThreadInfo>();

            var jsonDoc = JsonDocument.Parse(output);
            var threads = new List<ProcessThreadInfo>();

            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var thread in jsonDoc.RootElement.EnumerateArray())
                {
                    threads.Add(new ProcessThreadInfo
                    {
                        ThreadId = thread.TryGetProperty("Id", out var idProp) ? idProp.GetInt32() : 0,
                        TotalProcessorTime = thread.TryGetProperty("TotalProcessorTime", out var tptProp) ? tptProp.GetString() : "N/A",
                        State = thread.TryGetProperty("State", out var stateProp) ? stateProp.GetString() : "Unknown",
                        WaitReason = thread.TryGetProperty("WaitReason", out var wrProp) ? wrProp.GetString() : "N/A"
                    });
                }
            }

            return threads;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero thread processo {ProcessId}", processId);
            return new List<ProcessThreadInfo>();
        }
    }

    public async Task<List<ProcessModuleInfo>> GetProcessModules(int processId)
    {
        try
        {
            var script = $"Get-Process -Id {processId} -Module | Select-Object FileName, Size, Company, ProductVersion | ConvertTo-Json";
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output))
                return new List<ProcessModuleInfo>();

            var jsonDoc = JsonDocument.Parse(output);
            var modules = new List<ProcessModuleInfo>();

            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var module in jsonDoc.RootElement.EnumerateArray())
                {
                    modules.Add(new ProcessModuleInfo
                    {
                        FileName = module.TryGetProperty("FileName", out var fnProp) ? fnProp.GetString() : "Unknown",
                        Size = module.TryGetProperty("Size", out var sizeProp) ? sizeProp.GetInt64() : 0,
                        Company = module.TryGetProperty("Company", out var compProp) ? compProp.GetString() : "Unknown",
                        ProductVersion = module.TryGetProperty("ProductVersion", out var pvProp) ? pvProp.GetString() : "Unknown"
                    });
                }
            }

            return modules;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero moduli processo {ProcessId}", processId);
            return new List<ProcessModuleInfo>();
        }
    }

    private async Task<string> RunPowerShellScript(string script)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'esecuzione script PowerShell");
            return string.Empty;
        }
    }
}

public class ProcessInvestigationResult
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string CpuTime { get; set; } = string.Empty;
    public long WorkingSet { get; set; }
    public long PrivateMemory { get; set; }
    public int HandleCount { get; set; }
    public int ThreadCount { get; set; }
    public DateTime StartTime { get; set; }
    public string Path { get; set; } = string.Empty;
}

public class ProcessHandleInfo
{
    public int ProcessId { get; set; }
    public int HandleCount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ProcessThreadInfo
{
    public int ThreadId { get; set; }
    public string TotalProcessorTime { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string WaitReason { get; set; } = string.Empty;
}

public class ProcessModuleInfo
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Company { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
}
