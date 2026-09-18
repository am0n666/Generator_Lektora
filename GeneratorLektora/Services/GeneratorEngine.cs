using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeneratorLektora.Models;

namespace GeneratorLektora.Services;

public sealed class GeneratorEngine
{
    private readonly ProcessRunner _runner = new();
    private readonly string _root;

    public GeneratorEngine(string root) => _root = root;

    public async Task RunAsync(GeneratorOptions o, IProgress<ProgressInfo> progress, CancellationToken token)
    {
        var tools = Path.Combine(_root, "tools");
        var ffmpeg = Path.Combine(tools, "ffmpeg", "bin", "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffmpeg", "bin", "ffprobe.exe");
        var python = Path.Combine(tools, "python", "python.exe");
        var script = Path.Combine(tools, "lektor.py");

        var missing = new System.Collections.Generic.List<string>();
        if (!File.Exists(ffmpeg)) missing.Add("FFmpeg");
        if (!File.Exists(ffprobe)) missing.Add("FFprobe");
        if (!File.Exists(python)) missing.Add("Python 3.11");
        if (!File.Exists(script)) missing.Add("lektor.py");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Brakuje komponentów wymaganych do generowania: " + string.Join(", ", missing) +
                ".\n\nUruchom „Instalacja / naprawa komponentów”.");
        }

        progress.Report(new("Uruchamianie silnika Supertonic 3...", 2));
        var args = new StringBuilder();
        args.Append($"-W ignore::DeprecationWarning \"{script}\"");
        args.Append($" --input \"{o.Input}\" --output \"{o.Output}\"");
        args.Append($" --ffmpeg \"{ffmpeg}\" --ffprobe \"{ffprobe}\"");
        args.Append($" --voice \"{o.Voice}\" --steps {o.Steps.ToString(CultureInfo.InvariantCulture)}");
        args.Append($" --max-speed {o.MaxSpeed.ToString(CultureInfo.InvariantCulture)}");
        args.Append($" --base-speed {o.BaseSpeed.ToString(CultureInfo.InvariantCulture)}");
        args.Append($" --duck-threshold {o.DuckThreshold.ToString(CultureInfo.InvariantCulture)}");
        args.Append($" --min-pause {o.MinPause.ToString(CultureInfo.InvariantCulture)}");
        if (o.SmartPauses) args.Append(" --smart-pauses");
        else args.Append(" --no-smart-pauses");
        if (o.NoDuck) args.Append(" --no-duck");
        if (!string.IsNullOrWhiteSpace(o.Srt)) args.Append($" --srt \"{o.Srt}\"");

        var rc = await _runner.RunAsync(python, args.ToString(), tools, line =>
        {
            var pct = ParsePercent(line);
            progress.Report(new(line, pct));
        }, token);

        if (rc != 0)
            throw new InvalidOperationException($"Generator zakończył pracę kodem {rc}. Szczegóły znajdują się w logu.");
        progress.Report(new("Gotowe — plik został wygenerowany.", 100));
    }

    private static double? ParsePercent(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3})%");
        return m.Success && double.TryParse(m.Groups[1].Value, out var p) ? Math.Clamp(p, 0, 100) : null;
    }
}

public sealed record ProgressInfo(string Message, double? Percent);
