using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GeneratorLektora.Models;

namespace GeneratorLektora.Services;

public sealed class ComponentInstaller
{
    private readonly string _root;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public ComponentInstaller(string root) => _root = root;

    public async Task InstallAsync(IProgress<InstallProgress> progress, CancellationToken token)
    {
        var tools = Path.Combine(_root, "tools");
        Directory.CreateDirectory(tools);
        var ffDir = Path.Combine(tools, "ffmpeg");
        var ffExe = Path.Combine(ffDir, "bin", "ffmpeg.exe");

        if (!File.Exists(ffExe))
        {
            Report(progress, "FFmpeg", "Pobieranie", "Pobieranie najnowszego wydania FFmpeg…", null);
            var zip = Path.Combine(tools, "ffmpeg.zip");
            await Download("https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip", zip, progress, "FFmpeg", 0, 25, token);
            Report(progress, "FFmpeg", "Instalacja", "Rozpakowywanie FFmpeg…", 27, indeterminate: true);
            var tmp = Path.Combine(tools, "ffmpeg_tmp");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp, true);
            var first = Directory.GetDirectories(tmp).FirstOrDefault();
            if (first is null) throw new InvalidOperationException("Nieprawidłowe archiwum FFmpeg.");
            if (Directory.Exists(ffDir)) Directory.Delete(ffDir, true);
            Directory.Move(first, ffDir);
            Directory.Delete(tmp, true);
            File.Delete(zip);
        }
        else Report(progress, "FFmpeg", "Gotowe", "FFmpeg jest już zainstalowany.", 25);

        var pyDir = Path.Combine(tools, "python");
        var pyExe = Path.Combine(pyDir, "python.exe");
        if (!File.Exists(pyExe))
        {
            Report(progress, "Python 3.11", "Pobieranie", "Pobieranie wersji embeddable…", null);
            var zip = Path.Combine(tools, "python.zip");
            await Download("https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip", zip, progress, "Python 3.11", 30, 20, token);
            Report(progress, "Python 3.11", "Instalacja", "Rozpakowywanie środowiska Python…", 48, indeterminate: true);
            Directory.CreateDirectory(pyDir);
            ZipFile.ExtractToDirectory(zip, pyDir, true);
            File.Delete(zip);
        }
        else Report(progress, "Python 3.11", "Gotowe", "Python jest już zainstalowany.", 50);

        Report(progress, "Python 3.11", "Konfiguracja", "Konfiguracja site-packages…", 52, indeterminate: true);
        foreach (var pth in Directory.GetFiles(pyDir, "*._pth"))
        {
            var c = await File.ReadAllTextAsync(pth, token);
            if (!c.Split('\n').Any(x => x.Trim() == "import site")) c += Environment.NewLine + "import site" + Environment.NewLine;
            if (!c.Split('\n').Any(x => x.Trim().Replace('/', '\\') == @"Lib\site-packages")) c += Environment.NewLine + @"Lib\site-packages" + Environment.NewLine;
            await File.WriteAllTextAsync(pth, c, new System.Text.UTF8Encoding(false), token);
        }
        Directory.CreateDirectory(Path.Combine(pyDir, "Lib", "site-packages"));

        var pip = Path.Combine(pyDir, "get-pip.py");
        if (!await CanRun(pyExe, "-m pip --version", pyDir, token))
        {
            Report(progress, "pip", "Pobieranie", "Pobieranie bootstrapu pip…", null);
            await Download("https://bootstrap.pypa.io/get-pip.py", pip, progress, "pip", 52, 10, token);
            Report(progress, "pip", "Instalacja", "Instalowanie pip…", 63, indeterminate: true);
            await Run(pyExe, $"\"{pip}\" --no-warn-script-location", pyDir, line => progress.Report(new InstallProgress("pip", "Instalacja", line, 63, 0, null, 0, true)), token);
            File.Delete(pip);
        }
        else Report(progress, "pip", "Gotowe", "pip jest już zainstalowany.", 65);

        if (!await CanRun(pyExe, "-c \"import supertonic, soundfile, onnxruntime, numpy\"", pyDir, token))
        {
            Report(progress, "Supertonic 3", "Instalacja", "Instalowanie Supertonic 3 i zależności…", 70, indeterminate: true);
            await Run(pyExe, "-m pip install --no-warn-script-location supertonic soundfile onnxruntime numpy", pyDir,
                line => progress.Report(new InstallProgress("Supertonic 3", "Instalacja", line, 70, 0, null, 0, true)), token);
        }
        Report(progress, "Supertonic 3", "Gotowe", "Supertonic 3 i zależności są gotowe.", 100);
        Report(progress, "Komponenty", "Zakończono", "Wszystkie komponenty są gotowe do pracy.", 100);
    }

    private async Task Download(string url, string target, IProgress<InstallProgress> progress, string component, double startPercent, double spanPercent, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(target);
        var buffer = new byte[128 * 1024];
        long received = 0;
        var started = Stopwatch.GetTimestamp();
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            var elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            var speed = elapsed > 0 ? received / elapsed : 0;
            double? pct = total is long t && t > 0 ? startPercent + (received * spanPercent / t) : null;
            var message = total is long bytes ? $"Pobieranie… {FormatBytes(received)} z {FormatBytes(bytes)}" : $"Pobieranie… {FormatBytes(received)}";
            progress.Report(new InstallProgress(component, "Pobieranie", message, pct, received, total, speed));
        }
    }

    private static void Report(IProgress<InstallProgress> p, string component, string stage, string message, double? percent, long received = 0, long? total = null, double speed = 0, bool indeterminate = false)
        => p.Report(new InstallProgress(component, stage, message, percent, received, total, speed, indeterminate));

    private static async Task<bool> CanRun(string exe, string args, string wd, CancellationToken token)
    {
        try { return await Run(exe, args, wd, null, token) == 0; } catch { return false; }
    }

    private static async Task<int> Run(string exe, string args, string wd, Action<string>? log, CancellationToken token)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = wd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = new Process { StartInfo = psi };
        p.Start();
        try
        {
            var stdout = Pump(p.StandardOutput, log, token);
            var stderr = Pump(p.StandardError, log, token);
            await Task.WhenAll(stdout, stderr, p.WaitForExitAsync(token));
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
            throw;
        }
        if (p.ExitCode != 0) throw new InvalidOperationException($"Proces zakończył się kodem {p.ExitCode}.");
        return p.ExitCode;
    }

    private static async Task Pump(System.IO.StreamReader reader, Action<string>? log, CancellationToken token)
    {
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is not null) log?.Invoke(line);
        }
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.0} {units[i]}";
    }
}
