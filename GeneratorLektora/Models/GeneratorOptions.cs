namespace GeneratorLektora.Models;

public sealed class GeneratorOptions
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string? Srt { get; set; }
    public string Voice { get; set; } = "M3";
    public int Steps { get; set; } = 12;
    public double BaseSpeed { get; set; } = 1.00;
    public double MaxSpeed { get; set; } = 1.20;
    public double DuckThreshold { get; set; } = 0.05;
    public bool NoDuck { get; set; }
    public bool SmartPauses { get; set; } = true;
    public double MinPause { get; set; } = 0.20;
}
