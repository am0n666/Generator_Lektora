using System;
using System.Windows;
using GeneratorLektora.Models;

namespace GeneratorLektora;

public partial class InstallationWindow : Window
{
    private bool _canClose;

    public InstallationWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { };
    }

    public void UpdateProgress(InstallProgress p)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateProgress(p)); return; }
        ComponentText.Text = p.Component;
        StageText.Text = p.Stage;
        SetComponentState(p.Component, p.Stage);
        DetailText.Text = p.Message;
        if (p.IsIndeterminate)
        {
            ProgressBar.IsIndeterminate = true;
            PercentText.Text = "…";
        }
        else
        {
            ProgressBar.IsIndeterminate = false;
            if (p.Percent is double pct)
            {
                ProgressBar.Value = Math.Clamp(pct, 0, 100);
                PercentText.Text = $"{pct:0}%";
            }
        }
        SpeedText.Text = p.BytesPerSecond > 0 ? $"{FormatBytes(p.BytesPerSecond)}/s" : "";
        SizeText.Text = p.TotalBytes is long total
            ? $"{FormatBytes(p.BytesReceived)} / {FormatBytes(total)}"
            : p.BytesReceived > 0 ? FormatBytes(p.BytesReceived) : "";
    }

    public void Complete(bool success, string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Complete(success, message)); return; }
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = success ? 100 : ProgressBar.Value;
        PercentText.Text = success ? "100%" : "";
        StatusText.Text = success ? "Gotowe" : "Błąd / anulowano";
        DetailText.Text = message;
        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        CloseButton.Content = "Zamknij";
    }

    public void AllowClose() => _canClose = true;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_canClose) Close();
        else
        {
            CloseButton.IsEnabled = false;
            StatusText.Text = "Anulowanie…";
            // MainWindow owns the cancellation token; closing this window is intentionally deferred until the task exits.
            if (Owner is MainWindow main) main.RequestCancellation();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
            if (CloseButton.IsEnabled)
            {
                CloseButton.IsEnabled = false;
                StatusText.Text = "Anulowanie…";
                if (Owner is MainWindow main) main.RequestCancellation();
            }
            return;
        }
        base.OnClosing(e);
    }

    private void SetComponentState(string component, string stage)
    {
        var active = component switch
        {
            "FFmpeg" => FfmpegState,
            "Python 3.11" => PythonState,
            "pip" => PipState,
            "Supertonic 3" => SupertonicState,
            _ => null
        };
        if (active is not null) active.Text = stage;
        if (component != "FFmpeg") FfmpegState.Text = FfmpegState.Text == "Oczekuje" ? "Oczekuje" : FfmpegState.Text;
        if (stage == "Gotowe")
        {
            if (active is not null) active.Text = "Gotowe";
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
