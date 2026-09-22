using System;
using System.Drawing;
using System.Drawing.Imaging;
using PdfiumViewer;

namespace PDF2CBZ.Services;

public class PdfRenderer : IDisposable
{
    // Потолок битмапа: выше Pdfium/GDI+ либо молча чернит, либо падает.
    // Замерено: 226 Мп ещё рендерится, 300+ Мп — ошибка/чёрный экран.
    private const long MaxRenderPixels = 150_000_000;

    private PdfDocument? _document;
    private bool _disposed;

    public int PageCount => _document?.PageCount ?? 0;

    // Фактический DPI последнего рендера (может быть ниже запрошенного)
    public int LastRenderDpi { get; private set; } = 150;

    public void Load(string pdfPath)
    {
        if (_document != null)
        {
            _document.Dispose();
            _document = null;
        }
        _document = PdfDocument.Load(pdfPath);
    }

    public Bitmap RenderPage(int pageIndex, int dpi = 150)
    {
        if (_document == null)
            throw new InvalidOperationException("PDF document not loaded");

        if (pageIndex < 0 || pageIndex >= _document.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        // ВАЖНО: width/height здесь — пункты (1/72 дюйма), библиотека сама
        // умножает на dpi/72. Передача готовых пикселей давала двойное
        // масштабирование и чёрные страницы на больших DPI.
        var pageSize = _document.PageSizes[pageIndex];
        var width = Math.Max(1, (int)Math.Round(pageSize.Width));
        var height = Math.Max(1, (int)Math.Round(pageSize.Height));

        // Страховка: честный размер в пикселях, при превышении снижаем DPI
        long honestPixels = (long)Math.Round(pageSize.Width * dpi / 72.0)
                          * (long)Math.Round(pageSize.Height * dpi / 72.0);
        int actualDpi = dpi;
        if (honestPixels > MaxRenderPixels)
        {
            double scale = Math.Sqrt((double)MaxRenderPixels / honestPixels);
            actualDpi = Math.Max(72, (int)(dpi * scale));
        }
        LastRenderDpi = actualDpi;

        return (Bitmap)_document.Render(pageIndex, width, height, actualDpi, actualDpi, PdfRotation.Rotate0, PdfRenderFlags.CorrectFromDpi);
    }

    private static readonly ImageCodecInfo JpegEncoder = GetEncoder(ImageFormat.Jpeg);

    public void SavePageAsJpeg(int pageIndex, string outputPath, int quality, int dpi = 150)
    {
        using var bitmap = RenderPage(pageIndex, dpi);
        using var encoderParams = new EncoderParameters(1);
        using var qualityParam = new EncoderParameter(Encoder.Quality, (long)quality);
        encoderParams.Param[0] = qualityParam;
        bitmap.Save(outputPath, JpegEncoder, encoderParams);
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
        if (!_disposed)
        {
            _document?.Dispose();
            _document = null;
            _disposed = true;
        }
    }
}