namespace PDF2CBZ.Models;

public class ConversionSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public int JpegQuality { get; set; } = 90;
    public int RenderDpi { get; set; } = 150;
    public FileConflictAction ConflictAction { get; set; } = FileConflictAction.Ask;
}

public enum FileConflictAction
{
    Ask,
    Replace,
    Skip,
    Cancel
}