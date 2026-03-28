using System.Text.Json;
using PwlEditor.Models;
using System.IO;

namespace PwlEditor.Services;

public static class ProjectFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(string filePath, WaveProject project)
    {
        var json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static WaveProject Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<WaveProject>(json, JsonOptions)
               ?? throw new InvalidOperationException("Projektdatei konnte nicht geladen werden.");
    }
}
