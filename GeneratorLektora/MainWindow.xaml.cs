using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GeneratorLektora.Models;
using GeneratorLektora.Services;
using Microsoft.Win32;

namespace GeneratorLektora;

public partial class MainWindow : Window
{
    private readonly string _root;
    private readonly SettingsService _settingsService;
    private GeneratorSettings _settings;
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        _root = AppContext.BaseDirectory;
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        RefreshConfigurationSummary();
        LogBox.AppendText("Generator Lektora uruchomiony.\r\n");
        LogBox.AppendText("Wybierz film i plik wynikowy, a następnie kliknij „Generuj lektora”.\r\n");
    }

    private void InputBrowse_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Pliki wideo|*.mkv;*.mp4;*.avi;*.mov;*.m4v;*.webm|Wszystkie pliki|*.*" };
        if (d.ShowDialog() != true) return;
        InputBox.Text = d.FileName;
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
            OutputBox.Text = Path.Combine(Path.GetDirectoryName(d.FileName) ?? Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(d.FileName) + ".lektor.mkv");
    }

    private void OutputBrowse_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "Matroska MKV|*.mkv", DefaultExt = ".mkv", AddExtension = true };
        if (d.ShowDialog() == true) OutputBox.Text = d.FileName;
    }

    private void SrtBrowse_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Napisy|*.srt;*.ass;*.ssa;*.vtt|Wszystkie pliki|*.*" };
        if (d.ShowDialog() == true) SrtBox.Text = d.FileName;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            _settingsService.Save(_settings);
            RefreshConfigurationSummary();
        }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate(out var options)) return;

        SetBusy(true);
        ProgressBar.Value = 0;
        PercentText.Text = "0%";
        StageText.Text = "Przygotowywanie…";
        _cts = new CancellationTokenSource();

        try
        {
            var engine = new GeneratorEngine(_root);
            var token = _cts.Token;

            // Uruchamiamy cały silnik na wątku roboczym. Dzięki temu nawet synchroniczne
            // fragmenty biblioteki Pythona/IO nie mogą zablokować wątku WPF.
            await Task.Run(async () =>
            {
                // Progress jest tworzony na wątku roboczym, więc raportowanie z procesu
                // nie powoduje lawiny Dispatcher.Invoke na wątku UI.
                var progress = new ThrottledProgress<ProgressInfo>(
                    TimeSpan.FromMilliseconds(100),
                    p => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ApplyGeneratorProgress(p))));

                try
            {
                await engine.RunAsync(options!, progress, token).ConfigureAwait(false);
            }
            finally
            {
                progress.Dispose();
            }
            }, token).ConfigureAwait(true);

            StatusText.Text = "Zakończono pomyślnie";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            ProgressBar.Value = 100;
            PercentText.Text = "100%";
            StageText.Text = "Gotowe — plik został wygenerowany.";
            MessageBox.Show(this, $"Gotowe!\n\n{options!.Output}", "Generator Lektora", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Anulowano";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("WarningBrush");
            StageText.Text = "Operacja anulowana.";
            AppendLog("Operacja anulowana.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Błąd";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StageText.Text = "Wystąpił błąd.";
            AppendLog("BŁĄD: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Błąd generatora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void ApplyGeneratorProgress(ProgressInfo p)
    {
        if (p.Percent is double pct)
        {
            ProgressBar.Value = pct;
            PercentText.Text = $"{pct:0}%";
        }

        if (!string.IsNullOrWhiteSpace(p.Message))
            StageText.Text = p.Message;

        AppendLog(p.Message);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var window = new InstallationWindow { Owner = this };
        window.Show();
        try
        {
            var installer = new ComponentInstaller(_root);
            // Cały instalator działa poza wątkiem UI. Dzięki temu rozpakowywanie ZIP-ów,
            // operacje plikowe oraz procesy pip/FFmpeg nie mogą blokować interfejsu.
            // Progress<T> automatycznie wraca na wątek WPF tylko na czas aktualizacji GUI.
            var progress = new Progress<InstallProgress>(p => window.UpdateProgress(p));
            await Task.Run(() => installer.InstallAsync(progress, _cts.Token), _cts.Token);
            window.Complete(true, "Wszystkie komponenty są gotowe.");
            AppendLog("Komponenty FFmpeg, Python i Supertonic 3 są gotowe.");
        }
        catch (OperationCanceledException)
        {
            window.Complete(false, "Instalacja została anulowana.");
            AppendLog("Instalacja anulowana.");
        }
        catch (Exception ex)
        {
            window.Complete(false, ex.Message);
            AppendLog("BŁĄD instalacji: " + ex.Message);
        }
        finally
        {
            window.AllowClose();
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    public void RequestCancellation() => _cts?.Cancel();

    private bool Validate(out GeneratorOptions? options)
    {
        options = null;
        if (!File.Exists(InputBox.Text)) { MessageBox.Show(this, "Wybierz istniejący plik wejściowy.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (string.IsNullOrWhiteSpace(OutputBox.Text)) { MessageBox.Show(this, "Wybierz plik wynikowy.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (!string.IsNullOrWhiteSpace(SrtBox.Text) && !File.Exists(SrtBox.Text)) { MessageBox.Show(this, "Wybrany plik SRT nie istnieje.", "Brak pliku", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (File.Exists(OutputBox.Text) && string.Equals(Path.GetFullPath(InputBox.Text), Path.GetFullPath(OutputBox.Text), StringComparison.OrdinalIgnoreCase))
        { MessageBox.Show(this, "Plik wynikowy nie może być taki sam jak wejściowy.", "Nieprawidłowa ścieżka", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        if (_settings.BaseSpeed <= 0 || _settings.MaxSpeed <= 0 || _settings.DuckThreshold <= 0)
        { MessageBox.Show(this, "Jedno z ustawień tempa lub duckingu ma nieprawidłową wartość.", "Nieprawidłowe ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }

        options = new GeneratorOptions
        {
            Input = InputBox.Text,
            Output = OutputBox.Text,
            Srt = string.IsNullOrWhiteSpace(SrtBox.Text) ? null : SrtBox.Text,
            Voice = _settings.Voice,
            Steps = _settings.Steps,
            BaseSpeed = _settings.BaseSpeed,
            MaxSpeed = _settings.MaxSpeed,
            DuckThreshold = _settings.DuckThreshold,
            NoDuck = _settings.NoDuck,
            SmartPauses = _settings.SmartPauses,
            MinPause = _settings.MinPause
        };
        return true;
    }

    private void RefreshConfigurationSummary()
    {
        var duck = _settings.NoDuck ? "bez duckingu" : $"ducking {_settings.DuckThreshold.ToString("0.00", CultureInfo.InvariantCulture)}";
        var pauses = _settings.SmartPauses ? $"inteligentne pauzy {_settings.MinPause.ToString("0.00", CultureInfo.InvariantCulture)} s" : "stała pauza";
        ConfigSummary.Text = $"{_settings.Voice} • {_settings.Steps} steps • tempo {_settings.BaseSpeed.ToString("0.00", CultureInfo.InvariantCulture)} • dopasowanie {_settings.MaxSpeed.ToString("0.00", CultureInfo.InvariantCulture)} • {pauses} • {duck}";
    }

    private void SetBusy(bool busy)
    {
        GenerateButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        InstallButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        InputBox.IsEnabled = !busy;
        OutputBox.IsEnabled = !busy;
        SrtBox.IsEnabled = !busy;
        StatusText.Text = busy ? "Praca…" : "Gotowy do pracy"; StatusDot.Fill = (System.Windows.Media.Brush)FindResource(busy ? "AccentBrush" : "SuccessBrush");
    }

    private void AppendLog(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => AppendLog(text)));
            return;
        }
        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}


internal sealed class ThrottledProgress<T> : IProgress<T>, IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Action<T> _handler;
    private readonly object _gate = new();
    private T? _pending;
    private bool _scheduled;
    private bool _disposed;

    public ThrottledProgress(TimeSpan interval, Action<T> handler)
    {
        _interval = interval;
        _handler = handler;
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = value;
            if (_scheduled) return;
            _scheduled = true;
        }

        _ = FlushLaterAsync();
    }

    private async Task FlushLaterAsync()
    {
        await Task.Delay(_interval).ConfigureAwait(false);

        T? value;
        lock (_gate)
        {
            if (_disposed) return;
            value = _pending;
            _pending = default;
            _scheduled = false;
        }

        if (value is not null)
            _handler(value);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = default;
        }
    }
}
