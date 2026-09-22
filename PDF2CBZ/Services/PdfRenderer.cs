using System;
using System.Drawing;
using System.Drawing.Imaging;
using PdfiumViewer;

namespace PDF2CBZ.Services;

public class PdfRenderer : IDisposable
{
    private PdfDocument? _document;
    private bool _disposed;

    public int PageCount => _document?.PageCount ?? 0;

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

        var pageSize = _document.PageSizes[pageIndex];
        var width = (int)Math.Round(pageSize.Width / 72.0 * dpi);
        var height = (int)Math.Round(pageSize.Height / 72.0 * dpi);

        return (Bitmap)_document.Render(pageIndex, width, height, dpi, dpi, PdfRotation.Rotate0, PdfRenderFlags.CorrectFromDpi);
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