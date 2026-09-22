namespace PDF2CBZ.Models;

public class ConversionProgress
{
    public int CurrentFileIndex { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public int CurrentPage { get; set; }
    public int TotalPagesInCurrentFile { get; set; }
    public long TotalPagesOverall { get; set; }
    public long ProcessedPagesOverall { get; set; }
    public double OverallProgressPercent => TotalPagesOverall > 0 ? (double)ProcessedPagesOverall / TotalPagesOverall * 100 : 0;
    public double CurrentFileProgressPercent => TotalPagesInCurrentFile > 0 ? (double)CurrentPage / TotalPagesInCurrentFile * 100 : 0;
    public bool IsComplete { get; set; }
    public bool IsCancelled { get; set; }
    public string? ErrorMessage { get; set; }

    // New ETA and timing properties
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string ElapsedTimeFormatted => FormatTimeSpan(ElapsedTime);
    public string EstimatedTimeRemainingFormatted => EstimatedTimeRemaining.HasValue ? FormatTimeSpan(EstimatedTimeRemaining.Value) : "Расчёт времени...";
    public string OverallProgressSummary => $"{CurrentFileIndex} из {TotalFiles} файлов · {OverallProgressPercent:F0}%";
    public string TimeSummary => $"Прошло: {ElapsedTimeFormatted} · Осталось примерно: {EstimatedTimeRemainingFormatted}";

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}