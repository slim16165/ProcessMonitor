using System.Diagnostics;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class EverythingService
{
    private readonly string? _everythingPath;
    private const string DefaultEverythingPath = @"C:\Program Files\Everything\es.exe";

    public EverythingService(string? everythingPath = null)
    {
        _everythingPath = everythingPath ?? DefaultEverythingPath;
    }

    /// <summary>
    /// Verifica se Everything è disponibile
    /// </summary>
    public bool IsAvailable()
    {
        return !string.IsNullOrEmpty(_everythingPath) && File.Exists(_everythingPath);
    }

    /// <summary>
    /// Cerca directory .git ordinate per dimensione usando Everything
    /// </summary>
    public async Task<List<LargeGitRepository>> FindLargeGitDirsWithEverythingAsync(int topCount = 20)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Everything non è disponibile. Verifica il percorso in appsettings.json");
        }

        var gitDirs = new List<LargeGitRepository>();

        try
        {
            // Cerca tutte le directory .git
            var startInfo = new ProcessStartInfo
            {
                FileName = _everythingPath,
                Arguments = "-sort size -descending folder:*.git",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return gitDirs;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines.Take(topCount))
                {
                    var gitDir = line.Trim();
                    if (Directory.Exists(gitDir))
                    {
                        try
                        {
                            var repoPath = Directory.GetParent(gitDir)?.FullName ?? gitDir;
                            
                            // Calcola dimensione
                            var sizeMB = await GetDirectorySizeAsync(gitDir);
                            
                            gitDirs.Add(new LargeGitRepository
                            {
                                RepositoryPath = repoPath,
                                GitDirPath = gitDir,
                                GitDirSizeMB = sizeMB,
                                TotalSizeMB = sizeMB, // Approssimativo
                                IsLarge = sizeMB >= 500
                            });
                        }
                        catch
                        {
                            // Ignora errori
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Errore durante l'esecuzione di Everything: {ex.Message}", ex);
        }

        return gitDirs.OrderByDescending(r => r.GitDirSizeMB).ToList();
    }

    /// <summary>
    /// Cerca directory grandi usando Everything
    /// </summary>
    public async Task<List<string>> FindLargeDirectoriesAsync(string pattern = "*", int topCount = 20)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Everything non è disponibile");
        }

        var directories = new List<string>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _everythingPath,
                Arguments = $"-sort size -descending folder:{pattern}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return directories;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                directories.AddRange(lines.Take(topCount).Select(l => l.Trim()));
            }
        }
        catch
        {
            // Errore
        }

        return directories;
    }

    private async Task<long> GetDirectorySizeAsync(string directory)
    {
        try
        {
            var psScript = $@"
                $size = (Get-ChildItem -Path '{directory}' -Recurse -File -ErrorAction SilentlyContinue | 
                    Measure-Object -Property Length -Sum).Sum
                $sizeMB = [math]::Round($size / 1MB, 2)
                Write-Output $sizeMB
            ";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{psScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return 0;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (double.TryParse(output.Trim(), out var sizeMB))
            {
                return (long)sizeMB;
            }
        }
        catch
        {
            // Errore
        }

        return 0;
    }
}

