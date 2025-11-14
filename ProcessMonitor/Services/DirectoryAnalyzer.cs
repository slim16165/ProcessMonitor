using System.Collections.Concurrent;
using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class DirectoryAnalyzer
{
    private readonly ProcessMonitorConfig _config;

    public DirectoryAnalyzer(ProcessMonitorConfig config)
    {
        _config = config;
    }

    public async Task<DirectoryAnalysis> AnalyzeDirectoryAsync(
        string path, 
        int? maxDepth = null, 
        TimeSpan? timeout = null)
    {
        var analysis = new DirectoryAnalysis();
        var actualMaxDepth = maxDepth ?? _config.MaxDirectoryDepth;
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(_config.DirectoryAnalysisTimeoutSeconds);
        
        var cts = new CancellationTokenSource(actualTimeout);
        var largeDirs = new ConcurrentBag<string>();
        var fileCount = 0L;
        var totalSize = 0L;
        var maxDepthFound = 0;

        try
        {
            await Task.Run(() =>
            {
                AnalyzeDirectoryRecursive(
                    path, 
                    0, 
                    actualMaxDepth, 
                    cts.Token,
                    ref fileCount,
                    ref totalSize,
                    ref maxDepthFound,
                    largeDirs);
            }, cts.Token);
            
            analysis.AnalysisCompleted = true;
        }
        catch (OperationCanceledException)
        {
            analysis.TimeoutReason = $"Analisi interrotta dopo {actualTimeout.TotalSeconds} secondi";
        }
        catch (Exception ex)
        {
            analysis.TimeoutReason = $"Errore durante l'analisi: {ex.Message}";
        }

        analysis.FileCount = (int)fileCount;
        analysis.TotalSize = totalSize;
        analysis.MaxDepth = maxDepthFound;
        analysis.LargeDirectories = largeDirs.ToList();
        
        return analysis;
    }

    private void AnalyzeDirectoryRecursive(
        string directory,
        int currentDepth,
        int maxDepth,
        CancellationToken cancellationToken,
        ref long fileCount,
        ref long totalSize,
        ref int maxDepthFound,
        ConcurrentBag<string> largeDirs)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (currentDepth > maxDepth)
            return;

        if (currentDepth > maxDepthFound)
            maxDepthFound = currentDepth;

        try
        {
            var dirInfo = new DirectoryInfo(directory);
            
            // Verifica se è una directory esclusa
            if (_config.ExcludedDirectories.Any(excluded => 
                dirInfo.Name.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // Analizza file nella directory corrente
            var files = dirInfo.GetFiles("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            });

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    fileCount++;
                    totalSize += file.Length;
                }
                catch
                {
                    // Ignora errori di accesso ai file
                }
            }

            // Analizza sottodirectory
            var subdirs = dirInfo.GetDirectories("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            });

            foreach (var subdir in subdirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    AnalyzeDirectoryRecursive(
                        subdir.FullName,
                        currentDepth + 1,
                        maxDepth,
                        cancellationToken,
                        ref fileCount,
                        ref totalSize,
                        ref maxDepthFound,
                        largeDirs);
                }
                catch
                {
                    // Ignora errori di accesso alle directory
                }
            }

            // Rileva directory grandi
            if (fileCount > 10000)
            {
                largeDirs.Add(directory);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignora directory senza permessi
        }
        catch (DirectoryNotFoundException)
        {
            // Ignora directory non trovate
        }
    }

    public List<ProblematicDirectory> FindProblematicDirectories(string rootPath)
    {
        var problematic = new List<ProblematicDirectory>();
        
        try
        {
            var dirInfo = new DirectoryInfo(rootPath);
            var subdirs = dirInfo.GetDirectories("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                MaxRecursionDepth = 3 // Limita profondità per performance
            });

            foreach (var subdir in subdirs)
            {
                try
                {
                    var files = subdir.GetFiles("*", SearchOption.AllDirectories);
                    var fileCount = files.Length;
                    var totalSize = files.Sum(f => f.Length);
                    var depth = GetDirectoryDepth(subdir.FullName, rootPath);

                    var reasons = new List<string>();
                    
                    if (fileCount > 10000)
                        reasons.Add($"Troppi file ({fileCount:N0})");
                    
                    if (totalSize > 1024L * 1024 * 1024) // > 1GB
                        reasons.Add($"Dimensione elevata ({totalSize / (1024 * 1024):N0} MB)");
                    
                    if (depth > 10)
                        reasons.Add($"Profondità elevata ({depth} livelli)");

                    // Verifica se è una directory sincronizzata
                    if (subdir.Name.Equals("Dropbox", StringComparison.OrdinalIgnoreCase) ||
                        subdir.Name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
                    {
                        reasons.Add("Directory sincronizzata (può essere lenta)");
                    }

                    if (reasons.Any())
                    {
                        problematic.Add(new ProblematicDirectory
                        {
                            Path = subdir.FullName,
                            FileCount = fileCount,
                            Size = totalSize,
                            Depth = depth,
                            Reason = string.Join(", ", reasons)
                        });
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
            // Ignora errori
        }

        return problematic.OrderByDescending(p => p.Size).ToList();
    }

    private int GetDirectoryDepth(string path, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Count(p => !string.IsNullOrEmpty(p));
    }
}

