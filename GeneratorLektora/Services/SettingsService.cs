using System;
using System.IO;
using System.Text.Json;
using GeneratorLektora.Models;

namespace GeneratorLektora.Services;

public sealed class SettingsService
{
    private readonly string _file = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeneratorLektora", "settings.json");

    public GeneratorSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new GeneratorSettings();
            return JsonSerializer.Deserialize<GeneratorSettings>(File.ReadAllText(_file)) ?? new GeneratorSettings();
        }
        catch { return new GeneratorSettings(); }
    }

    public void Save(GeneratorSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
