using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GeneratorLektora.Services;

public sealed class ProcessRunner
{
    public async Task<int> RunAsync(string fileName, string arguments, string workingDirectory,
        Action<string>? output, CancellationToken token, ProcessPriorityClass? priority = null, long? affinityMask = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Start();

        // Proces generatora może intensywnie korzystać z CPU (ONNX/Supertonic).
        // Obniżamy jego priorytet, aby Windows pozostawił odpowiednią responsywność
        // dla interfejsu WPF, Eksploratora i innych aplikacji.
        try
        {
            p.PriorityClass = priority ?? ProcessPriorityClass.BelowNormal;
            if (affinityMask.HasValue && affinityMask.Value != 0)
                p.ProcessorAffinity = (IntPtr)affinityMask.Value;
        }
        catch
        {
            // Niektóre konfiguracje Windows mogą odmówić zmiany priorytetu.
        }

        try
        {
            var stdout = PumpAsync(p.StandardOutput, output, token);
            var stderr = PumpAsync(p.StandardError, output, token);
            await Task.WhenAll(stdout, stderr, p.WaitForExitAsync(token));
            return p.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch { /* proces mógł już zakończyć się sam */ }

            throw;
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string>? output, CancellationToken token)
    {
        while (!reader.EndOfStream)
        {
            token.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(token);
            if (line is not null) output?.Invoke(line);
        }
    }
}
