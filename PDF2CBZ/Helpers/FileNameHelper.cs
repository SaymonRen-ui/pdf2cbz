using System.IO;

namespace PDF2CBZ.Helpers;

public static class FileNameHelper
{
    public static string GetPageFileName(int pageNumber, int totalPages)
    {
        int digits = totalPages.ToString().Length;
        return pageNumber.ToString($"D{digits}") + ".jpg";
    }

    public static string GetOutputCbzPath(string pdfPath, string outputDirectory)
    {
        string fileName = Path.GetFileNameWithoutExtension(pdfPath);
        return Path.Combine(outputDirectory, $"{fileName}.cbz");
    }

    public static string GetTempCbzPath(string pdfPath, string outputDirectory)
    {
        string fileName = Path.GetFileNameWithoutExtension(pdfPath);
        return Path.Combine(outputDirectory, $"{fileName}.cbz.tmp");
    }
}