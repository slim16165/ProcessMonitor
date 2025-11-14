using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class ExternalToolsService
{
    private readonly ExternalToolsConfig _config;
    private readonly ILogger<ExternalToolsService>? _logger;

    public ExternalToolsService(ExternalToolsConfig config, ILogger<ExternalToolsService>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public bool AnalyzeWithWhatIsHang(int processId)
    {
        if (string.IsNullOrEmpty(_config.WhatIsHangPath) || !File.Exists(_config.WhatIsHangPath))
        {
            _logger?.LogWarning("WhatIsHang.exe non trovato: {Path}", _config.WhatIsHangPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.WhatIsHangPath,
                Arguments = $"/pid:{processId}",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(startInfo);
            _logger?.LogInformation("WhatIsHang avviato per PID {ProcessId}", processId);
            return process != null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'avvio di WhatIsHang per PID {ProcessId}", processId);
            return false;
        }
    }

    public bool AnalyzeWithUIHang(int processId)
    {
        if (string.IsNullOrEmpty(_config.UIHangPath) || !File.Exists(_config.UIHangPath))
        {
            _logger?.LogWarning("UIHang.exe non trovato: {Path}", _config.UIHangPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.UIHangPath,
                Arguments = $"{processId}",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(startInfo);
            _logger?.LogInformation("UIHang avviato per PID {ProcessId}", processId);
            return process != null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'avvio di UIHang per PID {ProcessId}", processId);
            return false;
        }
    }

    public bool OpenWithProcessExplorer(int processId)
    {
        if (string.IsNullOrEmpty(_config.ProcessExplorerPath) || !File.Exists(_config.ProcessExplorerPath))
        {
            _logger?.LogWarning("Process Explorer non trovato: {Path}", _config.ProcessExplorerPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.ProcessExplorerPath,
                Arguments = processId > 0 ? $"/p {processId}" : "",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(startInfo);
            if (processId > 0)
            {
                _logger?.LogInformation("Process Explorer avviato per PID {ProcessId}", processId);
            }
            else
            {
                _logger?.LogInformation("Process Explorer avviato");
            }
            return process != null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'avvio di Process Explorer per PID {ProcessId}", processId);
            return false;
        }
    }

    public bool OpenWithProcmon(int? processId = null)
    {
        if (string.IsNullOrEmpty(_config.ProcmonPath) || !File.Exists(_config.ProcmonPath))
        {
            _logger?.LogWarning("Procmon non trovato: {Path}", _config.ProcmonPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.ProcmonPath,
                Arguments = processId.HasValue ? $"/ProcessId {processId.Value}" : "",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(startInfo);
            _logger?.LogInformation("Procmon avviato {Arguments}", startInfo.Arguments);
            return process != null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'avvio di Procmon");
            return false;
        }
    }

    public async Task<string?> RunWhatIsHangAsync(int processId, int timeoutSeconds = 30)
    {
        if (string.IsNullOrEmpty(_config.WhatIsHangPath) || !File.Exists(_config.WhatIsHangPath))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.WhatIsHangPath,
                Arguments = $"/pid:{processId} /quiet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));

            if (!completed)
            {
                process.Kill();
                return "Timeout durante l'analisi";
            }

            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Errore nell'esecuzione di WhatIsHang");
            return $"Errore: {ex.Message}";
        }
    }

    public List<ExternalTool> GetAvailableTools()
    {
        var tools = new List<ExternalTool>();

        if (!string.IsNullOrEmpty(_config.WhatIsHangPath) && File.Exists(_config.WhatIsHangPath))
        {
            tools.Add(new ExternalTool
            {
                Name = "WhatIsHang",
                Path = _config.WhatIsHangPath,
                Description = "Analizza processi bloccati e freeze UI"
            });
        }

        if (!string.IsNullOrEmpty(_config.UIHangPath) && File.Exists(_config.UIHangPath))
        {
            tools.Add(new ExternalTool
            {
                Name = "UIHang",
                Path = _config.UIHangPath,
                Description = "Rileva UI hang e freeze"
            });
        }

        if (!string.IsNullOrEmpty(_config.ProcessExplorerPath) && File.Exists(_config.ProcessExplorerPath))
        {
            tools.Add(new ExternalTool
            {
                Name = "Process Explorer",
                Path = _config.ProcessExplorerPath,
                Description = "Visualizza dettagli processi avanzati"
            });
        }

        if (!string.IsNullOrEmpty(_config.ProcmonPath) && File.Exists(_config.ProcmonPath))
        {
            tools.Add(new ExternalTool
            {
                Name = "Process Monitor",
                Path = _config.ProcmonPath,
                Description = "Monitora attività file system, registry e network"
            });
        }

        return tools;
    }
}

