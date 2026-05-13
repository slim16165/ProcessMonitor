using System.Diagnostics;
using System.Text.Json;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public static class ProcessFingerprintService
{
    private static readonly Dictionary<int, ProcessFingerprint> _fingerprints = new();
    private const string FingerprintsFileName = "process_fingerprints.json";

    static ProcessFingerprintService()
    {
        LoadFingerprints();
    }

    public static ProcessFingerprint GetFingerprint(int processId)
    {
        if (_fingerprints.TryGetValue(processId, out var cached))
        {
            // Refresh if older than 5 minutes
            if (DateTime.Now - cached.LastSeen < TimeSpan.FromMinutes(5))
                return cached;
        }

        return CreateFingerprint(processId);
    }

    public static ProcessFingerprint CreateFingerprint(int processId)
    {
        var fingerprint = new ProcessFingerprint
        {
            ProcessId = processId,
            LastSeen = DateTime.Now
        };

        try
        {
            var process = Process.GetProcessById(processId);
            fingerprint.ProcessName = process.ProcessName;
            fingerprint.StartTime = process.StartTime;
            fingerprint.Duration = DateTime.Now - process.StartTime;

            try
            {
                fingerprint.Path = process.MainModule?.FileName;
            }
            catch { /* Access denied */ }

            try
            {
                fingerprint.CommandLine = GetCommandLine(processId);
            }
            catch { /* WMI may fail */ }

            try
            {
                var parent = GetParentProcess(processId);
                fingerprint.ParentProcessId = parent?.Id;
                fingerprint.ParentProcessName = parent?.ProcessName;
            }
            catch { /* WMI may fail */ }

            try
            {
                fingerprint.Publisher = GetProcessPublisher(processId);
            }
            catch { /* WMI may fail */ }

            try
            {
                fingerprint.WorkingDirectory = GetWorkingDirectory(processId);
            }
            catch { /* WMI may fail */ }

            try
            {
                fingerprint.UserSession = GetUserSession(processId);
            }
            catch { /* WMI may fail */ }

            try
            {
                fingerprint.FileHash = CalculateFileHash(fingerprint.Path);
            }
            catch { /* Hashing may fail */ }

            _fingerprints[processId] = fingerprint;
            SaveFingerprints();
        }
        catch (ArgumentException)
        {
            // Process not found
        }

        return fingerprint;
    }

    public static List<ProcessFingerprint> GetActiveFingerprints()
    {
        return _fingerprints.Values.Where(f => f.IsActive).ToList();
    }

    public static void RemoveFingerprint(int processId)
    {
        _fingerprints.Remove(processId);
        SaveFingerprints();
    }

    public static string GenerateFingerprintHash(ProcessFingerprint fingerprint)
    {
        // Create a stable hash based on key attributes
        var key = $"{fingerprint.ProcessName}|{fingerprint.Path}|{fingerprint.CommandLine}|{fingerprint.ParentProcessName}";
        return System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))
            .Take(8)
            .Aggregate("", (current, b) => current + b.ToString("x2"));
    }

    private static string? GetCommandLine(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"(Get-WmiObject Win32_Process -Filter 'ProcessId={processId}').CommandLine\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.StandardOutput.ReadToEnd()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static Process? GetParentProcess(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"(Get-WmiObject Win32_Process -Filter 'ProcessId={processId}').ParentProcessId\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            var parentIdStr = process?.StandardOutput.ReadToEnd()?.Trim();

            if (int.TryParse(parentIdStr, out var parentId))
            {
                return Process.GetProcessById(parentId);
            }
        }
        catch
        {
            // Parent process may not exist
        }

        return null;
    }

    private static string? GetProcessPublisher(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"(Get-ItemProperty (Get-WmiObject Win32_Process -Filter 'ProcessId={processId}').ExecutablePath).Company\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.StandardOutput.ReadToEnd()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWorkingDirectory(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"(Get-WmiObject Win32_Process -Filter 'ProcessId={processId}').ExecutablePath | ForEach-Object {{ Split-Path $_ }}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.StandardOutput.ReadToEnd()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetUserSession(int processId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"(Get-WmiObject Win32_Process -Filter 'ProcessId={processId}').GetOwner().User\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.StandardOutput.ReadToEnd()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? CalculateFileHash(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveFingerprints()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, FingerprintsFileName);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_fingerprints, options);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessFingerprintService] Error saving fingerprints: {ex.Message}");
        }
    }

    private static void LoadFingerprints()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var fullPath = Path.Combine(currentDir, FingerprintsFileName);
            
            if (!File.Exists(fullPath))
                return;

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<Dictionary<int, ProcessFingerprint>>(json);
            if (loaded != null)
            {
                _fingerprints.Clear();
                foreach (var kvp in loaded)
                {
                    // Check if process still exists
                    try
                    {
                        Process.GetProcessById(kvp.Key);
                        _fingerprints[kvp.Key] = kvp.Value;
                    }
                    catch (ArgumentException)
                    {
                        // Process no longer exists, skip
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessFingerprintService] Error loading fingerprints: {ex.Message}");
        }
    }
}

public class ProcessFingerprint
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? CommandLine { get; set; }
    public int? ParentProcessId { get; set; }
    public string? ParentProcessName { get; set; }
    public string? Publisher { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? UserSession { get; set; }
    public string? FileHash { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsActive => Duration < TimeSpan.FromHours(24);
}
