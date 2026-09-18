namespace GeneratorLektora.Models;

public sealed record InstallProgress(
    string Component,
    string Stage,
    string Message,
    double? Percent,
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    bool IsIndeterminate = false);
