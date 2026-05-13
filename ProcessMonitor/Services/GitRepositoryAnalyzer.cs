using System.Diagnostics;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class GitRepositoryAnalyzer
{
    private readonly ProcessMonitorConfig _config;
    private const string DuPath = @"C:\Windows\System32\du.exe";

    public GitRepositoryAnalyzer(ProcessMonitorConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Trova repository Git enormi usando du.exe (Windows)
    /// </summary>
    public async Task<List<LargeGitRepository>> FindLargeGitRepositoriesAsync(
        string rootPath, 
        int topCount = 10,
        long minSizeMB = 100)
    {
        var repositories = new List<LargeGitRepository>();

        try
        {
            // Metodo 1: Usa du.exe se disponibile (più veloce)
            if (File.Exists(DuPath))
            {
                repositories = await FindWithDuAsync(rootPath, topCount, minSizeMB);
            }
            else
            {
                // Metodo 2: Fallback a PowerShell/recursive search
                repositories = await FindWithPowerShellAsync(rootPath, topCount, minSizeMB);
            }
        }
        catch
        {
            // Se fallisce, prova con metodo alternativo
            repositories = await FindWithPowerShellAsync(rootPath, topCount, minSizeMB);
        }

        return repositories.OrderByDescending(r => r.GitDirSizeMB).Take(topCount).ToList();
    }

    /// <summary>
    /// Trova repository Git usando du.exe (Windows)
    /// </summary>
    private async Task<List<LargeGitRepository>> FindWithDuAsync(
        string rootPath, 
        int topCount, 
        long minSizeMB)
    {
        var repositories = new List<LargeGitRepository>();
        
        try
        {
            // Trova tutte le cartelle .git
            var gitDirs = await FindGitDirectoriesAsync(rootPath);
            
            foreach (var gitDir in gitDirs)
            {
                try
                {
                    var repoPath = Directory.GetParent(gitDir)?.FullName ?? gitDir;
                    
                    // Usa du.exe per calcolare la dimensione
                    var sizeMB = await GetDirectorySizeWithDuAsync(gitDir);
                    
                    if (sizeMB >= minSizeMB)
                    {
                        var repoInfo = await GetRepositoryInfoAsync(repoPath, gitDir, sizeMB);
                        if (repoInfo != null)
                        {
                            repositories.Add(repoInfo);
                        }
                    }
                }
                catch
                {
                    // Ignora errori su singoli repo
                }
            }
        }
        catch
        {
            // Fallback a PowerShell
        }

        return repositories;
    }

    /// <summary>
    /// Trova repository Git usando PowerShell (più lento ma più accurato)
    /// </summary>
    private async Task<List<LargeGitRepository>> FindWithPowerShellAsync(
        string rootPath, 
        int topCount, 
        long minSizeMB)
    {
        var repositories = new List<LargeGitRepository>();
        
        try
        {
            var gitDirs = await FindGitDirectoriesAsync(rootPath);
            
            foreach (var gitDir in gitDirs)
            {
                try
                {
                    var repoPath = Directory.GetParent(gitDir)?.FullName ?? gitDir;
                    
                    // Calcola dimensione con PowerShell
                    var sizeMB = await GetDirectorySizeWithPowerShellAsync(gitDir);
                    
                    if (sizeMB >= minSizeMB)
                    {
                        var repoInfo = await GetRepositoryInfoAsync(repoPath, gitDir, sizeMB);
                        if (repoInfo != null)
                        {
                            repositories.Add(repoInfo);
                        }
                    }
                }
                catch
                {
                    // Ignora errori
                }
            }
        }
        catch
        {
            // Errore generale
        }

        return repositories;
    }

    /// <summary>
    /// Trova tutte le directory .git ricorsivamente
    /// </summary>
    private async Task<List<string>> FindGitDirectoriesAsync(string rootPath, int maxDepth = 5)
    {
        var gitDirs = new List<string>();
        
        await Task.Run(() =>
        {
            try
            {
                FindGitDirectoriesRecursive(rootPath, 0, maxDepth, gitDirs);
            }
            catch
            {
                // Ignora errori
            }
        });

        return gitDirs;
    }

    private void FindGitDirectoriesRecursive(string path, int currentDepth, int maxDepth, List<string> results)
    {
        if (currentDepth > maxDepth)
            return;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            
            // Cerca .git nella directory corrente
            var gitDir = Path.Combine(path, ".git");
            if (Directory.Exists(gitDir))
            {
                results.Add(gitDir);
                return; // Non scendere più in profondità se trovato .git
            }

            // Cerca nelle sottodirectory (escludi .git se già trovato)
            var subdirs = dirInfo.GetDirectories("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            });

            foreach (var subdir in subdirs)
            {
                // Salta directory esclusa
                if (_config.ExcludedDirectories.Any(excluded => 
                    subdir.Name.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                FindGitDirectoriesRecursive(subdir.FullName, currentDepth + 1, maxDepth, results);
            }
        }
        catch
        {
            // Ignora errori di accesso
        }
    }

    /// <summary>
    /// Calcola dimensione directory usando du.exe
    /// </summary>
    private async Task<long> GetDirectorySizeWithDuAsync(string directory)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = DuPath,
                Arguments = $"-q \"{directory}\"",
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

            // du.exe output format: "size\tpath"
            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    var firstLine = lines[0].Trim();
                    var parts = firstLine.Split('\t');
                    if (parts.Length >= 1 && long.TryParse(parts[0], out var size))
                    {
                        return size / (1024 * 1024); // Converti in MB
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        return 0;
    }

    /// <summary>
    /// Calcola dimensione directory usando PowerShell
    /// </summary>
    private async Task<long> GetDirectorySizeWithPowerShellAsync(string directory)
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

    /// <summary>
    /// Ottiene informazioni dettagliate su un repository Git
    /// </summary>
    private async Task<LargeGitRepository?> GetRepositoryInfoAsync(
        string repoPath, 
        string gitDir, 
        long gitDirSizeMB)
    {
        try
        {
            // Verifica che sia un repository Git valido
            var isValid = await IsValidGitRepositoryAsync(repoPath);
            if (!isValid)
                return null;

            // Conta file tracciati (approssimativo)
            var fileCount = await GetTrackedFileCountAsync(repoPath);
            
            // Calcola dimensione totale del repo (non solo .git)
            var totalSizeMB = await GetDirectorySizeWithPowerShellAsync(repoPath);

            return new LargeGitRepository
            {
                RepositoryPath = repoPath,
                GitDirPath = gitDir,
                GitDirSizeMB = gitDirSizeMB,
                TotalSizeMB = totalSizeMB,
                TrackedFileCount = fileCount,
                IsLarge = gitDirSizeMB >= 500 // > 500MB considerato grande
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifica se è un repository Git valido
    /// </summary>
    private async Task<bool> IsValidGitRepositoryAsync(string repoPath)
    {
        try
        {
            var gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir))
                return false;

            // Verifica presenza di file chiave
            var configFile = Path.Combine(gitDir, "config");
            return File.Exists(configFile);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Conta i file tracciati nel repository
    /// </summary>
    private async Task<int> GetTrackedFileCountAsync(string repoPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = "ls-files",
                WorkingDirectory = repoPath,
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

            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length;
            }
        }
        catch
        {
            // Ignora errori
        }

        return 0;
    }
}

