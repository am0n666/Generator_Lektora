using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Media;
using GeneratorLektora.Models;
using GeneratorLektora.Services;

namespace GeneratorLektora;

public partial class VoiceTestWindow : Window
{
    private readonly string _root;
    private readonly GeneratorSettings _settings;
    private readonly ProcessRunner _runner = new();
    private CancellationTokenSource? _cts;
    private SoundPlayer? _player;
    private string? _currentAudio;

    public VoiceTestWindow(GeneratorSettings settings)
    {
        InitializeComponent();
        _root = AppContext.BaseDirectory;
        _settings = settings;

        AddVoice("M1", "M1  •  męski");
        AddVoice("M2", "M2  •  męski");
        AddVoice("M3", "M3  •  męski  •  domyślny");
        AddVoice("M4", "M4  •  męski");
        AddVoice("M5", "M5  •  męski");
        AddVoice("F1", "F1  •  żeński");
        AddVoice("F2", "F2  •  żeński");
        AddVoice("F3", "F3  •  żeński");
        AddVoice("F4", "F4  •  żeński");
        AddVoice("F5", "F5  •  żeński");

        SelectVoice(settings.Voice);
        QualityText.Text = $"{settings.Steps} steps • tempo bazowe {settings.BaseSpeed.ToString("0.00", CultureInfo.InvariantCulture)}";
        TextBox.Text = "Dzień dobry. To jest przykładowy test głosu lektora. Sprawdzamy naturalność, tempo oraz wymowę języka polskiego.";
        UpdateCharacterCount();
    }

    private void AddVoice(string tag, string content) => VoiceBox.Items.Add(new ComboBoxItem { Tag = tag, Content = content });

    private void SelectVoice(string voice)
    {
        foreach (ComboBoxItem item in VoiceBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), voice, StringComparison.OrdinalIgnoreCase))
            {
                VoiceBox.SelectedItem = item;
                return;
            }
        }
        VoiceBox.SelectedIndex = 2;
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCharacterCount();

    private void UpdateCharacterCount() => CharacterCountText.Text = $"{TextBox.Text.Length}/1000";

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        var text = TextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "Wpisz tekst, który ma przeczytać lektor.", "Test głosu", MessageBoxButton.OK, MessageBoxImage.Warning);
            TextBox.Focus();
            return;
        }

        if (text.Length < 2)
        {
            MessageBox.Show(this, "Tekst jest zbyt krótki do sensownego testu.", "Test głosu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (VoiceBox.SelectedItem is not ComboBoxItem voiceItem)
            return;

        var tools = Path.Combine(_root, "tools");
        var python = Path.Combine(tools, "python", "python.exe");
        var script = Path.Combine(tools, "voice_test.py");

        if (!File.Exists(python) || !File.Exists(script))
        {
            MessageBox.Show(this, "Brakuje komponentów potrzebnych do testowania głosu. Uruchom „Instalacja / naprawa komponentów”.",
                "Test głosu", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StopPlayback();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = "Ładowanie modelu i generowanie próbki…";
        StatusDot.Fill = (Brush)FindResource("AccentBrush");

        var textFile = Path.Combine(Path.GetTempPath(), $"GeneratorLektora_voice_{Guid.NewGuid():N}.txt");
        var output = Path.Combine(Path.GetTempPath(), $"GeneratorLektora_voice_{Guid.NewGuid():N}.wav");
        await File.WriteAllTextAsync(textFile, text, new System.Text.UTF8Encoding(false), _cts.Token);

        try
        {
            var args = $"-W ignore::DeprecationWarning \"{script}\" --text-file \"{textFile}\" --output \"{output}\" --voice \"{voiceItem.Tag}\" --steps {_settings.Steps.ToString(CultureInfo.InvariantCulture)} --speed {_settings.BaseSpeed.ToString(CultureInfo.InvariantCulture)}";
            var lastUpdate = DateTime.UtcNow;
            var rc = await _runner.RunAsync(python, args, tools, line =>
            {
                if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds >= 100)
                {
                    lastUpdate = DateTime.UtcNow;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => StatusText.Text = line));
                }
            }, _cts.Token);

            if (rc != 0 || !File.Exists(output))
                throw new InvalidOperationException("Nie udało się wygenerować próbki głosu. Sprawdź log konsoli instalatora lub ponownie zainstaluj komponenty.");

            _currentAudio = output;
            StatusText.Text = $"Ładowanie próbki do odtwarzacza: {voiceItem.Tag}";
            StatusDot.Fill = (Brush)FindResource("SuccessBrush");

            // SoundPlayer ładuje WAV asynchronicznie. MediaPlayer.Open() jest
            // synchroniczne i przy większej próbce potrafił chwilowo blokować wątek WPF.
            _player = new SoundPlayer(output);
            _player.LoadCompleted += Player_LoadCompleted;
            _player.LoadAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Anulowano.";
            StatusDot.Fill = (Brush)FindResource("WarningBrush");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Nie udało się wygenerować próbki.";
            StatusDot.Fill = (Brush)FindResource("DangerBrush");
            MessageBox.Show(this, ex.Message, "Test głosu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TryDelete(textFile);
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void Player_LoadCompleted(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Error is not null)
            {
                ClosePlayer();
                StatusText.Text = "Nie udało się załadować próbki.";
                StatusDot.Fill = (Brush)FindResource("DangerBrush");
                return;
            }

            try
            {
                _player?.Play();
                StatusText.Text = "Odtwarzanie próbki…";
                StatusDot.Fill = (Brush)FindResource("SuccessBrush");
                StopButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ClosePlayer();
                StatusText.Text = "Nie udało się odtworzyć próbki.";
                StatusDot.Fill = (Brush)FindResource("DangerBrush");
                MessageBox.Show(this, ex.Message, "Test głosu", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StopPlayback();
        StatusText.Text = "Zatrzymano.";
        StatusDot.Fill = (Brush)FindResource("WarningBrush");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        VoiceBox.IsEnabled = !busy;
        TextBox.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy || _player is not null;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StopPlayback()
    {
        ClosePlayer();
        CleanupAudioFile();
    }

    private void ClosePlayer()
    {
        if (_player is null) return;
        try
        {
            _player.LoadCompleted -= Player_LoadCompleted;
            _player.Stop();
        }
        catch { }
        _player = null;
        CleanupAudioFile();
        StopButton.IsEnabled = false;
    }

    private void CleanupAudioFile()
    {
        if (_currentAudio is null) return;
        var file = _currentAudio;
        _currentAudio = null;
        TryDelete(file);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        StopPlayback();
        _cts?.Dispose();
        _cts = null;
    }
}
