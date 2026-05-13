using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcessMonitor.Services;

public class WindowsSystemProcessClassifier
{
    private readonly Dictionary<string, WindowsSystemProcess> _systemProcesses;

    public WindowsSystemProcessClassifier(string jsonPath)
    {
        _systemProcesses = LoadSystemProcesses(jsonPath);
    }

    public bool IsWindowsSystemProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;
        
        var normalized = NormalizeProcessName(processName);
        return _systemProcesses.ContainsKey(normalized);
    }

    public string? GetSystemCategory(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;
        
        var normalized = NormalizeProcessName(processName);
        return _systemProcesses.TryGetValue(normalized, out var process) 
            ? process.Category 
            : null;
    }

    public string? GetSystemDescription(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;
        
        var normalized = NormalizeProcessName(processName);
        return _systemProcesses.TryGetValue(normalized, out var process)
            ? process.Description
            : null;
    }

    private static Dictionary<string, WindowsSystemProcess> LoadSystemProcesses(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath))
                return new Dictionary<string, WindowsSystemProcess>();

            var json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var data = JsonSerializer.Deserialize<WindowsSystemProcessesData>(json, options);
            
            if (data?.SystemProcesses == null)
                return new Dictionary<string, WindowsSystemProcess>();
            
            return data.SystemProcesses
                .ToDictionary(
                    p => NormalizeProcessName(p.Name),
                    p => p
                );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsSystemProcessClassifier] Error loading JSON: {ex.Message}");
            return new Dictionary<string, WindowsSystemProcess>();
        }
    }

    private static string NormalizeProcessName(string processName) 
        => processName.Trim().ToLowerInvariant();
}

internal class WindowsSystemProcessesData
{
    [JsonPropertyName("system_processes")]
    public List<WindowsSystemProcess>? SystemProcesses { get; set; }
}

internal class WindowsSystemProcess
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
