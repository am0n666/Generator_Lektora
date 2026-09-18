using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GeneratorLektora.Models;

namespace GeneratorLektora;

public partial class SettingsWindow : Window
{
    public GeneratorSettings Settings { get; private set; }

    public SettingsWindow(GeneratorSettings current)
    {
        InitializeComponent();
        Settings = new GeneratorSettings
        {
            Voice = current.Voice, Steps = current.Steps, BaseSpeed = current.BaseSpeed,
            MaxSpeed = current.MaxSpeed, DuckThreshold = current.DuckThreshold, NoDuck = current.NoDuck,
            SmartPauses = current.SmartPauses, MinPause = current.MinPause
        };
        SelectByTag(VoiceBox, Settings.Voice);
        SelectByTag(StepsBox, Settings.Steps.ToString(CultureInfo.InvariantCulture));
        BaseSpeedBox.Text = Settings.BaseSpeed.ToString("0.00", CultureInfo.InvariantCulture);
        MaxSpeedBox.Text = Settings.MaxSpeed.ToString("0.00", CultureInfo.InvariantCulture);
        DuckThresholdBox.Text = Settings.DuckThreshold.ToString("0.00", CultureInfo.InvariantCulture);
        NoDuckBox.IsChecked = Settings.NoDuck;
        SmartPausesBox.IsChecked = Settings.SmartPauses;
        MinPauseBox.Text = Settings.MinPause.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
        box.SelectedIndex = 0;
    }

    private void TestVoice_Click(object sender, RoutedEventArgs e)
    {
        var voice = (VoiceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? Settings.Voice;
        var previewSettings = new GeneratorSettings
        {
            Voice = voice,
            Steps = GetSelectedSteps(),
            BaseSpeed = ParseSpeed(BaseSpeedBox.Text, Settings.BaseSpeed),
            MaxSpeed = Settings.MaxSpeed,
            DuckThreshold = Settings.DuckThreshold,
            NoDuck = Settings.NoDuck,
            SmartPauses = Settings.SmartPauses, MinPause = Settings.MinPause
        };

        var window = new VoiceTestWindow(previewSettings) { Owner = this };
        window.ShowDialog();
    }

    private int GetSelectedSteps()
    {
        return StepsBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var steps)
            ? steps
            : Settings.Steps;
    }

    private static double ParseSpeed(string text, double fallback)
    {
        return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        SelectByTag(VoiceBox, "M3");
        SelectByTag(StepsBox, "12");
        BaseSpeedBox.Text = "1.00";
        MaxSpeedBox.Text = "1.20";
        DuckThresholdBox.Text = "0.05";
        NoDuckBox.IsChecked = false;
        SmartPausesBox.IsChecked = true;
        MinPauseBox.Text = "0.20";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(BaseSpeedBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var baseSpeed) || baseSpeed < 0.85 || baseSpeed > 1.30)
        { MessageBox.Show(this, "Bazowe tempo musi mieścić się w zakresie 0,85–1,30.", "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!double.TryParse(MaxSpeedBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxSpeed) || maxSpeed < 1.00 || maxSpeed > 1.60)
        { MessageBox.Show(this, "Maksymalne tempo dopasowania musi mieścić się w zakresie 1,00–1,60.", "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!double.TryParse(DuckThresholdBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var duck) || duck < 0.01 || duck > 0.20)
        { MessageBox.Show(this, "Próg duckingu musi mieścić się w zakresie 0,01–0,20.", "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!double.TryParse(MinPauseBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var minPause) || minPause < 0.08 || minPause > 0.50)
        { MessageBox.Show(this, "Minimalna pauza musi mieścić się w zakresie 0,08–0,50 s.", "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (VoiceBox.SelectedItem is not ComboBoxItem voiceItem || StepsBox.SelectedItem is not ComboBoxItem stepsItem)
        { MessageBox.Show(this, "Wybierz głos i jakość.", "Ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        Settings = new GeneratorSettings
        {
            Voice = voiceItem.Tag?.ToString() ?? "M3",
            Steps = int.Parse(stepsItem.Tag?.ToString() ?? "12", CultureInfo.InvariantCulture),
            BaseSpeed = baseSpeed,
            MaxSpeed = maxSpeed,
            DuckThreshold = duck,
            NoDuck = NoDuckBox.IsChecked == true,
            SmartPauses = SmartPausesBox.IsChecked == true,
            MinPause = minPause
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
