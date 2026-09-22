using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using PdfiumViewer;
using PDF2CBZ.Themes;
using SD = System.Drawing;

namespace PDF2CBZ;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up global exception handling FIRST
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        
        // Initialize theme manager (loads saved theme) before windows are created
        _ = ThemeManager.Instance;

        // Point PdfiumViewer at the native pdfium.dll, including the .NET
        // single-file extraction directory (verified: without this, a bundled
        // single exe fails with "Dll was not found"). No-op for regular builds.
        PdfiumResolver.Resolve += OnPdfiumResolve;

        base.OnStartup(e);
        
        // Generate app icon if not exists
        GenerateAppIcon();
    }

    private static void OnPdfiumResolve(object sender, PdfiumResolveEventArgs e)
    {
        var path = FindPdfiumLibrary();
        if (path != null)
            e.PdfiumFileName = path;
        // else: leave default probing (regular build layout).
    }

    private static string? FindPdfiumLibrary()
    {
        string subdir = IntPtr.Size == 8 ? "x64" : "x86";

        // 1) Beside the executable (regular build, loose publish).
        string nextToApp = Path.Combine(AppContext.BaseDirectory, subdir, "pdfium.dll");
        if (File.Exists(nextToApp))
            return nextToApp;

        // 2) .NET single-file extraction: %TEMP%\.net\<AppName>\<hash>\,
        // honoring the DOTNET_BUNDLE_EXTRACT_BASE_DIR override.
        try
        {
            string? extractBase = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
            extractBase ??= Path.Combine(Path.GetTempPath(), ".net");
            string appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "PDF2CBZ";
            string appDir = Path.Combine(extractBase, appName);
            if (Directory.Exists(appDir))
            {
                foreach (var hashDir in Directory.GetDirectories(appDir))
                {
                    string candidate = Path.Combine(hashDir, subdir, "pdfium.dll");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }
        catch
        {
            // Ignore - fall back to default probing.
        }

        return null;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"UI Thread Exception:\n{e.Exception}\n\nStack Trace:\n{e.Exception.StackTrace}", 
            "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        this.Shutdown(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        if (ex != null)
        {
            MessageBox.Show($"Background Thread Exception:\n{ex}\n\nStack Trace:\n{ex.StackTrace}", 
                "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Environment.Exit(-1);
    }

    private static void GenerateAppIcon()
    {
        var iconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDF2CBZ", "app.ico");
        
        var dir = Path.GetDirectoryName(iconPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(iconPath))
            return;

        try
        {
            const int size = 256;
            using var bitmap = new SD.Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = SD.Graphics.FromImage(bitmap);
            g.Clear(SD.Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Background circle
            using var bgBrush = new SD.SolidBrush(SD.Color.FromArgb(255, 0x00, 0x78, 0xD4));
            g.FillEllipse(bgBrush, 16, 16, size - 32, size - 32);

            // PDF icon (red rectangle with folded corner)
            var pdfRect = new SD.Rectangle(size / 2 - 40, size / 2 - 60, 80, 100);
            using var pdfBrush = new SD.SolidBrush(SD.Color.FromArgb(255, 0xE8, 0x11, 0x23));
            g.FillRoundedRectangle(pdfBrush, pdfRect, 8);

            // Folded corner
            var foldPoints = new[]
            {
                new SD.Point(pdfRect.Right - 20, pdfRect.Top),
                new SD.Point(pdfRect.Right, pdfRect.Top),
                new SD.Point(pdfRect.Right, pdfRect.Top + 20)
            };
            using var foldBrush = new SD.SolidBrush(SD.Color.FromArgb(255, 0xC0, 0x0E, 0x1E));
            g.FillPolygon(foldBrush, foldPoints);

            // "PDF" text
            using var font = new SD.Font("Segoe UI", 24, SD.FontStyle.Bold);
            using var textBrush = new SD.SolidBrush(SD.Color.White);
            var textSize = g.MeasureString("PDF", font);
            g.DrawString("PDF", font, textBrush, 
                pdfRect.X + (pdfRect.Width - textSize.Width) / 2,
                pdfRect.Y + 15);

            // Arrow pointing right
            var arrowStartX = pdfRect.Right + 20;
            var arrowCenterY = size / 2;
            using var arrowPen = new SD.Pen(SD.Color.White, 6) 
            { 
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(arrowPen, arrowStartX, arrowCenterY, arrowStartX + 40, arrowCenterY);
            // Arrow head
            g.DrawLine(arrowPen, arrowStartX + 40, arrowCenterY, arrowStartX + 25, arrowCenterY - 12);
            g.DrawLine(arrowPen, arrowStartX + 40, arrowCenterY, arrowStartX + 25, arrowCenterY + 12);

            // CBZ/ZIP icon (blue rectangle with zipper)
            var cbzRect = new SD.Rectangle(arrowStartX + 50, size / 2 - 60, 80, 100);
            using var cbzBrush = new SD.SolidBrush(SD.Color.FromArgb(255, 0x00, 0x78, 0xD4));
            g.FillRoundedRectangle(cbzBrush, cbzRect, 8);

            // Zipper lines
            using var zipPen = new SD.Pen(SD.Color.White, 3);
            for (int i = 0; i < 4; i++)
            {
                int y = cbzRect.Y + 25 + i * 18;
                g.DrawLine(zipPen, cbzRect.X + 15, y, cbzRect.Right - 15, y);
            }

            // Save as ICO with multiple sizes
            var iconSizes = new[] { 256, 128, 64, 48, 32, 16 };
            using var iconStream = new MemoryStream();
            
            using var writer = new BinaryWriter(iconStream);
            writer.Write((short)0);           // Reserved
            writer.Write((short)1);           // Type: 1 = ICO
            writer.Write((short)iconSizes.Length); // Number of images

            var imageData = new byte[iconSizes.Length][];

            for (int i = 0; i < iconSizes.Length; i++)
            {
                int iconSize = iconSizes[i];
                using var resized = ResizeImage(bitmap, iconSize, iconSize);
                using var ms = new MemoryStream();
                resized.Save(ms, ImageFormat.Png);
                imageData[i] = ms.ToArray();

                // Write icon directory entry
                writer.Write((byte)iconSize);      // Width
                writer.Write((byte)iconSize);      // Height
                writer.Write((byte)0);             // Color count (0 = no palette)
                writer.Write((byte)0);             // Reserved
                writer.Write((short)1);            // Color planes
                writer.Write((short)32);           // Bits per pixel
                writer.Write(imageData[i].Length); // Image size
                writer.Write(0);                   // Offset placeholder
            }

            // Fix offsets and write image data
            long currentOffset = 6 + 16 * iconSizes.Length;
            for (int i = 0; i < iconSizes.Length; i++)
            {
                // Go back and fix offset
                long pos = writer.BaseStream.Position;
                writer.BaseStream.Seek(6 + 16 * i + 12, SeekOrigin.Begin);
                writer.Write(currentOffset);
                writer.BaseStream.Seek(pos, SeekOrigin.Begin);

                writer.Write(imageData[i]);
                currentOffset += imageData[i].Length;
            }

            File.WriteAllBytes(iconPath, iconStream.ToArray());
        }
        catch (Exception ex)
        {
            // Log but don't crash
            System.Diagnostics.Debug.WriteLine($"Icon generation failed: {ex.Message}");
        }
    }

    private static SD.Bitmap ResizeImage(SD.Bitmap source, int width, int height)
    {
        var dest = new SD.Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(source, 0, 0, width, height);
        return dest;
    }
}

public static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this SD.Graphics g, SD.Brush brush, SD.Rectangle rect, int radius)
    {
        using var path = new SD.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}