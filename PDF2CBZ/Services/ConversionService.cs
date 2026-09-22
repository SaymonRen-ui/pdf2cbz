using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using PDF2CBZ.Helpers;
using PDF2CBZ.Models;

namespace PDF2CBZ.Services;

public class ConversionService : IDisposable
{
    // Кэшируем JPEG-энкодер один раз вместо поиска на каждой странице
    private static readonly ImageCodecInfo JpegEncoder = GetEncoder(ImageFormat.Jpeg);

    public event Action<ConversionProgress>? ProgressChanged;
    public event Action<string, string>? FileCompleted;
    public event Action<string, Exception>? FileError;

    public async Task ConvertAsync(
        IEnumerable<string> pdfFiles,
        ConversionSettings settings,
        CancellationToken cancellationToken)
    {
        var files = pdfFiles.ToList();

        await Task.Run(() => ConvertInternal(files, settings, cancellationToken), cancellationToken);
    }

    private void ConvertInternal(
        List<string> files,
        ConversionSettings settings,
        CancellationToken cancellationToken)
    {
        // Use local instances for thread safety
        using var pdfRenderer = new PdfRenderer();
        using var cbzCreator = new CbzCreator();

        var progress = new ConversionProgress
        {
            TotalFiles = files.Count
        };

        var stopwatch = Stopwatch.StartNew();
        var fileStopwatch = new Stopwatch();
        var completedFiles = 0;
        var fileTimes = new List<TimeSpan>();

        // Calculate total pages for overall progress
        long totalPages = 0;
        var filePageCounts = new Dictionary<string, int>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                pdfRenderer.Load(file);
                int count = pdfRenderer.PageCount;
                filePageCounts[file] = count;
                totalPages += count;
            }
            catch (Exception ex)
            {
                ReportError(file, ex);
            }
        }

        progress.TotalPagesOverall = totalPages;
        UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
        ReportProgress(progress);

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pdfFile = files[i];
            progress.CurrentFileIndex = i + 1;
            progress.CurrentFileName = Path.GetFileName(pdfFile);
            progress.TotalPagesInCurrentFile = filePageCounts.TryGetValue(pdfFile, out int count) ? count : 0;
            progress.CurrentPage = 0;

            fileStopwatch.Restart();
            UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
            ReportProgress(progress);

            string tempCbzPath = FileNameHelper.GetTempCbzPath(pdfFile, settings.OutputDirectory);
            string finalCbzPath = FileNameHelper.GetOutputCbzPath(pdfFile, settings.OutputDirectory);

            // Handle existing file
            if (File.Exists(finalCbzPath))
            {
                var action = settings.ConflictAction;
                if (action == FileConflictAction.Skip)
                {
                    progress.ProcessedPagesOverall += progress.TotalPagesInCurrentFile;
                    UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
                    ReportProgress(progress);
                    ReportFileCompleted(pdfFile, "Skipped (already exists)");
                    continue;
                }
                else if (action == FileConflictAction.Cancel)
                {
                    progress.IsCancelled = true;
                    UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
                    ReportProgress(progress);
                    return;
                }
                // Replace: continue to overwrite
            }

            int fileMinDpi = settings.RenderDpi;

            try
            {
                pdfRenderer.Load(pdfFile);
                cbzCreator.Create(tempCbzPath);

                int pageCount = pdfRenderer.PageCount;

                for (int page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress.CurrentPage = page + 1;
                    UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
                    ReportProgress(progress);

                    string pageFileName = FileNameHelper.GetPageFileName(page + 1, pageCount);

                    using var bitmap = pdfRenderer.RenderPage(page, settings.RenderDpi);
                    if (pdfRenderer.LastRenderDpi < fileMinDpi)
                        fileMinDpi = pdfRenderer.LastRenderDpi;
                    using var encoderParams = new EncoderParameters(1);
                    using var qualityParam = new EncoderParameter(Encoder.Quality, (long)settings.JpegQuality);
                    encoderParams.Param[0] = qualityParam;
                    using var outputMs = new MemoryStream();
                    bitmap.Save(outputMs, JpegEncoder, encoderParams);

                    cbzCreator.AddPage(pageFileName, outputMs.ToArray());

                    progress.ProcessedPagesOverall++;
                    UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
                    ReportProgress(progress);
                }

                cbzCreator.Complete(finalCbzPath);
                fileStopwatch.Stop();
                fileTimes.Add(fileStopwatch.Elapsed);
                completedFiles++;

                // Если страницы-гиганты уронили DPI — честно пишем в статус
                string doneMessage = fileMinDpi < settings.RenderDpi
                    ? $"Success (DPI {fileMinDpi} вместо {settings.RenderDpi}: страницы слишком большие)"
                    : "Success";
                ReportFileCompleted(pdfFile, doneMessage);
            }
            catch (OperationCanceledException)
            {
                cbzCreator.Cancel();
                throw;
            }
            catch (Exception ex)
            {
                cbzCreator.Cancel();
                ReportError(pdfFile, ex);
                // Continue with next file unless cancelled
            }
        }

        stopwatch.Stop();
        progress.IsComplete = true;
        UpdateProgress(progress, stopwatch, fileTimes, completedFiles);
        ReportProgress(progress);
    }

    private void UpdateProgress(ConversionProgress progress, Stopwatch stopwatch, List<TimeSpan> fileTimes, int completedFiles)
    {
        progress.ElapsedTime = stopwatch.Elapsed;

        // Calculate ETA based on completed files
        if (completedFiles > 0 && progress.TotalFiles > completedFiles)
        {
            var avgTimePerFile = TimeSpan.FromTicks(fileTimes.Sum(t => t.Ticks) / fileTimes.Count);
            var remainingFiles = progress.TotalFiles - completedFiles;
            var eta = TimeSpan.FromTicks(avgTimePerFile.Ticks * remainingFiles);
            progress.EstimatedTimeRemaining = eta;
        }
        else
        {
            progress.EstimatedTimeRemaining = null;
        }

        // Only show ETA after at least one file is completed
        if (completedFiles == 0)
        {
            progress.EstimatedTimeRemaining = null;
        }
    }

    private void ReportProgress(ConversionProgress progress)
    {
        ProgressChanged?.Invoke(progress);
    }

    private void ReportFileCompleted(string filePath, string message)
    {
        FileCompleted?.Invoke(filePath, message);
    }

    private void ReportError(string filePath, Exception ex)
    {
        FileError?.Invoke(filePath, ex);
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageDecoders())
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        throw new InvalidOperationException($"No encoder found for format {format}");
    }

    public void Dispose()
    {
        // No shared resources to dispose - each conversion creates its own instances
    }
}