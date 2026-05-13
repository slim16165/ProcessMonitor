using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class EventLogReaderService
{
    private readonly ILogger<EventLogReaderService>? _logger;
    private readonly InvestigationConfig _config;

    public EventLogReaderService(InvestigationConfig config, ILogger<EventLogReaderService>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<List<EventLogEntry>> GetProcessErrors(int processId, TimeSpan lookback)
    {
        var entries = new List<EventLogEntry>();
        
        try
        {
            var script = $@"
                $lookback = (Get-Date).AddHours(-{(lookback.TotalHours)})
                Get-EventLog -LogName Application -After $lookback | 
                Where-Object {{ $_.Message -match '{processId}' }} | 
                Select-Object TimeGenerated, Source, EntryType, Message, EventID | 
                ConvertTo-Json
            ";
            
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output))
                return entries;

            var jsonDoc = JsonDocument.Parse(output);
            
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in jsonDoc.RootElement.EnumerateArray())
                {
                    entries.Add(new EventLogEntry
                    {
                        Timestamp = entry.TryGetProperty("TimeGenerated", out var timeProp) ? timeProp.GetDateTime() : DateTime.MinValue,
                        Source = entry.TryGetProperty("Source", out var sourceProp) ? sourceProp.GetString() : "Unknown",
                        EntryType = entry.TryGetProperty("EntryType", out var typeProp) ? typeProp.GetString() : "Unknown",
                        Message = entry.TryGetProperty("Message", out var msgProp) ? msgProp.GetString() : "",
                        EventId = entry.TryGetProperty("EventID", out var idProp) ? idProp.GetInt32() : 0,
                        LogName = "Application"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero log Application per processo {ProcessId}", processId);
        }

        return entries;
    }

    public async Task<List<EventLogEntry>> GetSystemErrors(TimeSpan lookback)
    {
        var entries = new List<EventLogEntry>();
        
        try
        {
            var script = $@"
                $lookback = (Get-Date).AddHours(-{(lookback.TotalHours)})
                Get-EventLog -LogName System -After $lookback -EntryType Error, Warning | 
                Select-Object TimeGenerated, Source, EntryType, Message, EventID | 
                ConvertTo-Json
            ";
            
            var output = await RunPowerShellScript(script);
            
            if (string.IsNullOrEmpty(output))
                return entries;

            var jsonDoc = JsonDocument.Parse(output);
            
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in jsonDoc.RootElement.EnumerateArray())
                {
                    entries.Add(new EventLogEntry
                    {
                        Timestamp = entry.TryGetProperty("TimeGenerated", out var timeProp) ? timeProp.GetDateTime() : DateTime.MinValue,
                        Source = entry.TryGetProperty("Source", out var sourceProp) ? sourceProp.GetString() : "Unknown",
                        EntryType = entry.TryGetProperty("EntryType", out var typeProp) ? typeProp.GetString() : "Unknown",
                        Message = entry.TryGetProperty("Message", out var msgProp) ? msgProp.GetString() : "",
                        EventId = entry.TryGetProperty("EventID", out var idProp) ? idProp.GetInt32() : 0,
                        LogName = "System"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nel recupero log System");
        }

        return entries;
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

public class EventLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string LogName { get; set; } = string.Empty;
}

