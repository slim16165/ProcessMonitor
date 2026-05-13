using System.Text.Json;

namespace ProcessMonitor.Services;

public class ReportExporter
{
    public static void ExportToJson(object data, string outputPath)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(outputPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReportExporter] Error exporting to JSON: {ex.Message}");
            throw;
        }
    }

    public static void ExportToCsv<T>(IEnumerable<T> data, string outputPath)
    {
        try
        {
            if (data == null || !data.Any())
            {
                Console.WriteLine("[ReportExporter] No data to export to CSV");
                return;
            }

            var lines = new List<string>();
            var properties = typeof(T).GetProperties();
            
            // Header
            var header = string.Join(",", properties.Select(p => EscapeCsvField(p.Name)));
            lines.Add(header);
            
            // Data rows
            foreach (var item in data)
            {
                var values = properties.Select(p => 
                {
                    var value = p.GetValue(item);
                    return EscapeCsvField(value?.ToString() ?? string.Empty);
                });
                lines.Add(string.Join(",", values));
            }
            
            File.WriteAllLines(outputPath, lines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReportExporter] Error exporting to CSV: {ex.Message}");
            throw;
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;
        
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        
        return field;
    }
}
