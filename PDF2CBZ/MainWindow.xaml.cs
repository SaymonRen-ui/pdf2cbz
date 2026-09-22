using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PDF2CBZ.Controls;
using PDF2CBZ.Models;
using PDF2CBZ.Services;
using PDF2CBZ.Themes;

namespace PDF2CBZ;

public partial class MainWindow : CustomWindow
{
    private readonly ConversionService _conversionService = new();
    private readonly ObservableCollection<PdfFileInfo> _files = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private ConversionSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        FilesListBox.ItemsSource = _files;
        
        _conversionService.ProgressChanged += OnProgressChanged;
        _conversionService.FileCompleted += OnFileCompleted;
        _conversionService.FileError += OnFileError;
        
        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        Closing += MainWindow_Closing;
    }

    private bool _contentRenderedThemeApplied;

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        // Startup guarantee: re-apply DWM immersive mode + frame refresh after
        // the first frame is shown. If DWM snapshotted the frame before
        // OnSourceInitialized/Loaded, this corrects it without a resize.
        // Runs once; runtime theme switches go through ThemeManager.SetTheme.
        if (_contentRenderedThemeApplied) return;
        _contentRenderedThemeApplied = true;
        ThemeManager.ApplyThemeToWindow(this, ThemeManager.Instance.IsDark);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply DWM title bar theme (Dark/Light)
        ThemeManager.ApplyThemeToWindow(this, ThemeManager.Instance.IsDark);

        // Initialize theme button (icon + tooltip reflect the current theme)
        UpdateThemeButton();
        
        // Restore output directory
        OutputPathTextBox.Text = ThemeManager.Instance.OutputDirectory;
        
        UpdateStartButtonState();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            var result = MessageBox.Show("Конвертация в процессе. Вы уверены, что хотите выйти?", 
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
            _cancellationTokenSource.Cancel();
        }
        _conversionService.Dispose();
    }

    private void UpdateStartButtonState()
    {
        StartButton.IsEnabled = _files.Count > 0 && !string.IsNullOrWhiteSpace(_settings.OutputDirectory) 
            && Directory.Exists(_settings.OutputDirectory) && _cancellationTokenSource == null;
        UpdateFileCount();
    }

    private void UpdateFileCount()
    {
        FileCountText.Text = $"Добавлено файлов: {_files.Count}";
        EmptyStateText.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PDF файлы (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Выберите PDF файлы для конвертации"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                AddFile(file);
            }
            UpdateStartButtonState();
        }
    }

    private void AddFile(string filePath)
    {
        if (_files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            using var renderer = new PdfRenderer();
            renderer.Load(filePath);
            _files.Add(new PdfFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                PageCount = renderer.PageCount
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось добавить файл {Path.GetFileName(filePath)}:\n{ex.Message}", 
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        var selected = FilesListBox.SelectedItems.Cast<PdfFileInfo>().ToList();
        foreach (var file in selected)
        {
            _files.Remove(file);
        }
        UpdateStartButtonState();
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        _files.Clear();
        UpdateStartButtonState();
        UpdateFileCount();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                AddFile(file);
            }
            UpdateStartButtonState();
            UpdateFileCount();
        }
    }

    private void FilesListBox_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Any(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effects = DragDropEffects.Copy;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
    }

    private void FilesListBox_DragOver(object sender, DragEventArgs e)
    {
        FilesListBox_DragEnter(sender, e);
    }

    private void FilesListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files.Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            {
                AddFile(file);
            }
            UpdateStartButtonState();
            UpdateFileCount();
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения CBZ файлов",
            InitialDirectory = _settings.OutputDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            _settings.OutputDirectory = dialog.FolderName;
            ThemeManager.Instance.SetOutputDirectory(dialog.FolderName);
            OutputPathTextBox.Text = _settings.OutputDirectory;
            UpdateStartButtonState();
        }
    }

    private void OutputPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _settings.OutputDirectory = OutputPathTextBox.Text;
        UpdateStartButtonState();
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settings == null || QualityValueText == null) return;
        _settings.JpegQuality = (int)Math.Round(e.NewValue);
        QualityValueText.Text = _settings.JpegQuality.ToString();
    }

    private bool _syncingThemeMenu;

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        SyncThemeMenuSelection();
        ThemePopup.IsOpen = true;
        ThemeListBox.Focus();
    }

    private void SyncThemeMenuSelection()
    {
        // Pre-select the current theme without triggering apply/close logic.
        _syncingThemeMenu = true;
        try
        {
            foreach (var listItem in ThemeListBox.Items.OfType<ListBoxItem>())
            {
                if (listItem.Tag is string tag
                    && Enum.TryParse<AppTheme>(tag, out var theme)
                    && theme == ThemeManager.Instance.CurrentTheme)
                {
                    ThemeListBox.SelectedItem = listItem;
                    break;
                }
            }
        }
        finally
        {
            _syncingThemeMenu = false;
        }
    }

    private void ThemeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingThemeMenu) return;
        if (ThemeListBox.SelectedItem is ListBoxItem item && item.Tag is string tag)
        {
            if (Enum.TryParse<AppTheme>(tag, out var theme))
            {
                if (ThemeManager.Instance.CurrentTheme != theme)
                {
                    ThemeManager.Instance.SetTheme(theme);
                    UpdateThemeButton();
                }
            }
        }
        ThemePopup.IsOpen = false;
    }

    private void UpdateThemeButton()
    {
        if (ThemeButtonIcon == null || ThemeButton == null) return;
        var (icon, name) = ThemeManager.Instance.CurrentTheme switch
        {
            AppTheme.Light => ("☀", "Светлая"),
            AppTheme.Dark => ("☾", "Тёмная"),
            _ => ("◑", "Системная"),
        };
        ThemeButtonIcon.Text = icon;
        ThemeButton.ToolTip = $"Тема: {name}";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0 || string.IsNullOrWhiteSpace(_settings.OutputDirectory))
            return;

        if (!Directory.Exists(ThemeManager.Instance.OutputDirectory))
        {
            MessageBox.Show("Указанная папка назначения не существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Check for existing files
        var existingFiles = _files.Where(f => File.Exists(Path.Combine(ThemeManager.Instance.OutputDirectory, Path.GetFileNameWithoutExtension(f.FileName) + ".cbz"))).ToList();
        if (existingFiles.Count > 0 && _settings.ConflictAction == FileConflictAction.Ask)
        {
            var result = ShowConflictDialog(existingFiles.Count);
            if (result == MessageBoxResult.Cancel)
                return;
            
            _settings.ConflictAction = result == MessageBoxResult.Yes ? FileConflictAction.Replace : FileConflictAction.Skip;
        }

        SetUIState(true);
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            await _conversionService.ConvertAsync(_files.Select(f => f.FilePath), _settings, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Handled by progress
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Критическая ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            SetUIState(false);
            _settings.ConflictAction = FileConflictAction.Ask;
            UpdateStartButtonState();
        }
    }

    private MessageBoxResult ShowConflictDialog(int count)
    {
        var message = $"Найдено {count} файл(ов), для которых уже существуют CBZ файлы в папке назначения.\n\n" +
                      "Да — заменить существующие файлы\n" +
                      "Нет — пропустить эти файлы\n" +
                      "Отмена — прервать конвертацию";
        
        return MessageBox.Show(message, "Конфликт файлов", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        CancelButton.IsEnabled = false;
    }

    private void SetUIState(bool isConverting)
    {
        StartButton.IsEnabled = !isConverting;
        CancelButton.IsEnabled = isConverting;
        FilesListBox.IsEnabled = !isConverting;
        OutputPathTextBox.IsEnabled = !isConverting;
        QualitySlider.IsEnabled = !isConverting;
        ThemeButton.IsEnabled = !isConverting;
        
        // Disable file list buttons
        foreach (var child in FindVisualChildren<Button>(this))
        {
            if (child.Content is string text && (text == "Добавить PDF" || text == "Удалить" || text == "Очистить список" || text == "Обзор..."))
            {
                child.IsEnabled = !isConverting;
            }
        }
    }

    private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }
    }

    private void OnProgressChanged(ConversionProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            OverallProgressText.Text = progress.OverallProgressSummary;
            OverallPercentText.Text = $"{progress.OverallProgressPercent:F0}%";
            OverallProgressBar.Value = progress.OverallProgressPercent;

            TimeSummaryText.Text = progress.TimeSummary;

            if (progress.TotalPagesInCurrentFile > 0)
            {
                CurrentFileProgressText.Text = $"Страница {progress.CurrentPage} из {progress.TotalPagesInCurrentFile} ({progress.CurrentFileName})";
                CurrentFilePercentText.Text = $"{progress.CurrentFileProgressPercent:F1}%";
                CurrentFileProgressBar.Value = progress.CurrentFileProgressPercent;
            }
            else
            {
                CurrentFileProgressText.Text = progress.CurrentFileName;
                CurrentFilePercentText.Text = "";
                CurrentFileProgressBar.Value = 0;
            }

            ETASummaryText.Text = progress.EstimatedTimeRemainingFormatted;

            if (progress.IsCancelled)
            {
                OverallProgressText.Text = "Отменено";
                CurrentFileProgressText.Text = "";
                ETASummaryText.Text = "";
            }
            else if (progress.IsComplete)
            {
                OverallProgressText.Text = "Завершено";
                CurrentFileProgressText.Text = "";
                ETASummaryText.Text = "";
            }
        });
    }

    private void OnFileCompleted(string filePath, string message)
    {
        Dispatcher.Invoke(() =>
        {
            var file = _files.FirstOrDefault(f => f.FilePath == filePath);
            if (file != null)
            {
                file.Status = message;
            }
        });
    }

    private void OnFileError(string filePath, Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            var file = _files.FirstOrDefault(f => f.FilePath == filePath);
            if (file != null)
            {
                file.Status = $"Ошибка: {ex.Message}";
            }
        });
    }
}

public class PdfFileInfo : INotifyPropertyChanged
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    
    private string _status = "Ожидание";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}