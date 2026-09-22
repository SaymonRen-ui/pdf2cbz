using System.IO;
using System.IO.Compression;

namespace PDF2CBZ.Services;

public class CbzCreator : IDisposable
{
    private ZipArchive? _archive;
    private FileStream? _fileStream;
    private string _tempPath = string.Empty;
    private bool _disposed;
    private bool _completed;

    public void Create(string tempPath)
    {
        _tempPath = tempPath;
        _fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _archive = new ZipArchive(_fileStream, ZipArchiveMode.Create, true);
        _completed = false;
    }

    // JPEG уже сжат — deflate почти ничего не выигрывает, только жрёт CPU.
    // Стандарт для CBZ — Store (без сжатия): быстрее в разы, размер +доли %.
    private const CompressionLevel PageCompression = CompressionLevel.NoCompression;

    public void AddPage(string pageFileName, byte[] imageData)
    {
        if (_archive == null)
            throw new InvalidOperationException("CBZ archive not created");

        var entry = _archive.CreateEntry(pageFileName, PageCompression);
        using var entryStream = entry.Open();
        entryStream.Write(imageData, 0, imageData.Length);
    }

    public void AddPageFromFile(string pageFileName, string imageFilePath)
    {
        if (_archive == null)
            throw new InvalidOperationException("CBZ archive not created");

        var entry = _archive.CreateEntry(pageFileName, PageCompression);
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(imageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.CopyTo(entryStream);
    }

    public void Complete(string finalPath)
    {
        if (_archive == null || _fileStream == null)
            throw new InvalidOperationException("CBZ archive not created");

        _archive.Dispose();
        _archive = null;
        _fileStream.Dispose();
        _fileStream = null;

        if (File.Exists(finalPath))
        {
            File.Delete(finalPath);
        }
        File.Move(_tempPath, finalPath);
        _completed = true;
    }

    public void Cancel()
    {
        if (!_completed && !_disposed)
        {
            _archive?.Dispose();
            _archive = null;
            _fileStream?.Dispose();
            _fileStream = null;

            if (File.Exists(_tempPath))
            {
                try { File.Delete(_tempPath); } catch { }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (!_completed)
            {
                Cancel();
            }
            _disposed = true;
        }
    }
}