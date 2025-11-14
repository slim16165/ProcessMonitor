using ProcessMonitor.Models;

namespace ProcessMonitor.Services;

public class CommandAnalyzer
{
    private readonly ProcessMonitorConfig _config;

    public CommandAnalyzer(ProcessMonitorConfig config)
    {
        _config = config;
    }

    public CommandAnalysis AnalyzeCommand(string commandLine)
    {
        var analysis = new CommandAnalysis { CommandLine = commandLine };
        
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            analysis.RiskLevel = RiskLevel.Low;
            return analysis;
        }

        var cmdLower = commandLine.ToLowerInvariant();

        // Pattern pericolosi PowerShell
        if (cmdLower.Contains("get-childitem") || cmdLower.Contains("gci"))
        {
            if (cmdLower.Contains("-recurse") || cmdLower.Contains("-r"))
            {
                analysis.HasRecursiveSearch = true;
                
                // Verifica se ha limitazioni di profondità
                if (!cmdLower.Contains("-depth") && !cmdLower.Contains("-d "))
                {
                    analysis.HasNoDepthLimit = true;
                    analysis.Warnings.Add("Ricerca ricorsiva senza limite di profondità");
                }
                
                // Verifica se ha esclusioni
                if (!cmdLower.Contains("-exclude") && 
                    !cmdLower.Contains("-include") &&
                    !cmdLower.Contains("-filter"))
                {
                    analysis.HasNoExclusions = true;
                    analysis.Warnings.Add("Ricerca ricorsiva senza esclusioni per directory grandi");
                }
                
                // Verifica path pericoloso
                if (cmdLower.Contains("c:\\") || 
                    cmdLower.Contains("d:\\") ||
                    cmdLower.Contains("path c:") ||
                    cmdLower.Contains("path d:"))
                {
                    analysis.HasUnlimitedPath = true;
                    analysis.Warnings.Add("Ricerca su drive root (C:\\ o D:\\) - molto lento!");
                }
            }
        }

        // Pattern Python pericolosi
        if (cmdLower.Contains("python") || cmdLower.Contains("py "))
        {
            if (cmdLower.Contains("os.walk") && !cmdLower.Contains("topdown=false"))
            {
                analysis.Warnings.Add("os.walk() senza limitazioni potrebbe essere lento");
            }
            
            if (cmdLower.Contains("glob") && cmdLower.Contains("**") && !cmdLower.Contains("limit"))
            {
                analysis.Warnings.Add("glob con pattern ricorsivo senza limiti");
            }
        }

        // Pattern Node.js pericolosi
        if (cmdLower.Contains("node") || cmdLower.Contains("npm"))
        {
            if (cmdLower.Contains("find") && !cmdLower.Contains("maxdepth"))
            {
                analysis.Warnings.Add("Comando find senza limite di profondità");
            }
        }

        // Rileva operazioni senza timeout
        if ((cmdLower.Contains("subprocess") || cmdLower.Contains("process.start")) &&
            !cmdLower.Contains("timeout"))
        {
            analysis.Warnings.Add("Operazione subprocess senza timeout");
        }

        // Calcola livello di rischio
        analysis.RiskLevel = CalculateRiskLevel(analysis);
        
        return analysis;
    }

    private RiskLevel CalculateRiskLevel(CommandAnalysis analysis)
    {
        int riskScore = 0;

        if (analysis.HasRecursiveSearch)
            riskScore += 2;
        
        if (analysis.HasUnlimitedPath)
            riskScore += 3;
        
        if (analysis.HasNoDepthLimit)
            riskScore += 2;
        
        if (analysis.HasNoExclusions)
            riskScore += 1;

        return riskScore switch
        {
            >= 5 => RiskLevel.Critical,
            >= 3 => RiskLevel.High,
            >= 1 => RiskLevel.Medium,
            _ => RiskLevel.Low
        };
    }
}

