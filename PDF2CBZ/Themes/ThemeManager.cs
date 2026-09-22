using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PDF2CBZ.Themes;

public enum AppTheme
{
    Light,
    Dark,
    System
}

public class ThemeManager : INotifyPropertyChanged
{
    private static ThemeManager? _instance;
    public static ThemeManager Instance => _instance ??= new ThemeManager();

    private AppTheme _currentTheme = AppTheme.System;
    private bool _isSystemDark = false;
    private string _outputDirectory = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveTheme));
            }
        }
    }

    public AppTheme EffectiveTheme
    {
        get
        {
            if (_currentTheme == AppTheme.System)
                return _isSystemDark ? AppTheme.Dark : AppTheme.Light;
            return _currentTheme;
        }
    }

    public bool IsDark => EffectiveTheme == AppTheme.Dark;

    public string OutputDirectory
    {
        get => _outputDirectory;
        private set
        {
            if (_outputDirectory != value)
            {
                _outputDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    private const string SettingsFileName = "settings.json";
    private string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PDF2CBZ", SettingsFileName);

    private ThemeManager()
    {
        LoadSettings();
        _isSystemDark = IsSystemDark(); // Initialize with actual system theme
        ApplyTheme();
        RegisterSystemThemeChange();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var content = File.ReadAllText(SettingsFilePath).Trim();
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(content);
                if (settings != null)
                {
                    _currentTheme = settings.Theme;
                    _outputDirectory = settings.OutputDirectory ?? string.Empty;
                }
            }
        }
        catch
        {
            _currentTheme = AppTheme.System;
            _outputDirectory = string.Empty;
        }

        // If no saved output directory or it doesn't exist, use default
        if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
        {
            _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new AppSettings
            {
                Theme = _currentTheme,
                OutputDirectory = _outputDirectory
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SaveSettings();
        ApplyTheme();
    }

    public void SetOutputDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            OutputDirectory = directory;
            SaveSettings();
        }
    }

    private void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null) return;

        var resources = app.Resources.MergedDictionaries;

        string themeName = EffectiveTheme == AppTheme.Dark ? "DarkTheme" : "LightTheme";
        var themeUri = new Uri($"pack://application:,,,/PDF2CBZ;component/Themes/{themeName}.xaml", UriKind.Absolute);

        // Find existing theme dictionary in MergedDictionaries
        int themeIndex = -1;
        for (int i = 0; i < resources.Count; i++)
        {
            var src = resources[i].Source?.OriginalString;
            if (src != null && (src.Contains("LightTheme.xaml") || src.Contains("DarkTheme.xaml") || src.Contains("SystemTheme.xaml")))
            {
                themeIndex = i;
                break;
            }
        }

        var newThemeDict = new ResourceDictionary { Source = themeUri };

        if (themeIndex >= 0)
        {
            resources[themeIndex] = newThemeDict;
        }
        else
        {
            // Insert at index 1 (after Colors.xaml) or add if empty
            int insertIndex = Math.Min(1, resources.Count);
            resources.Insert(insertIndex, newThemeDict);
        }

        // Ensure Colors.xaml, CommonStyles.xaml, and WindowStyles.xaml are present
        EnsureDictionaryLoaded(resources, "Colors.xaml", 0);
        EnsureDictionaryLoaded(resources, "CommonStyles.xaml", resources.Count);
        EnsureDictionaryLoaded(resources, "WindowStyles.xaml", 2);

        // Update OS title bars on all open windows
        UpdateAllWindowsChrome();

        OnPropertyChanged(nameof(EffectiveTheme));
        OnPropertyChanged(nameof(IsDark));
    }

    private static void EnsureDictionaryLoaded(Collection<ResourceDictionary> resources, string fileName, int fallbackIndex)
    {
        // Remove existing dictionary with this filename if present (to reposition it)
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            var src = resources[i].Source?.OriginalString;
            if (src != null && src.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                resources.RemoveAt(i);
                break;
            }
        }

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PDF2CBZ;component/Themes/{fileName}", UriKind.Absolute)
        };

        if (fallbackIndex >= 0 && fallbackIndex <= resources.Count)
            resources.Insert(fallbackIndex, dict);
        else
            resources.Add(dict);
    }

    public void UpdateAllWindowsChrome()
    {
        var app = Application.Current;
        if (app == null) return;

        foreach (Window window in app.Windows)
        {
            ApplyThemeToWindow(window, IsDark);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_NCACTIVATE = 0x86;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void ApplyThemeToWindow(Window window, bool isDark)
    {
        if (window == null) return;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Window handle not yet created - subscribe to SourceInitialized
            EventHandler? onSourceInitialized = null;
            onSourceInitialized = (s, e) =>
            {
                window.SourceInitialized -= onSourceInitialized;
                var h = new WindowInteropHelper(window).Handle;
                if (h != IntPtr.Zero)
                {
                    SetWindowDarkMode(h, isDark);
                    RefreshWindowFrame(h);
                }
            };
            window.SourceInitialized += onSourceInitialized;
            return;
        }

        SetWindowDarkMode(hwnd, isDark);
        RefreshWindowFrame(hwnd);
    }

    private static void SetWindowDarkMode(IntPtr hwnd, bool isDark)
    {
        try
        {
            int useDarkMode = isDark ? 1 : 0;
            // Set both attribute IDs: 20 (20H1+) and 19 (pre-20H1).
            // Setting both unconditionally covers all Windows 10/11 builds;
            // a failing call on an unsupported ID is harmless.
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
        }
        catch
        {
            // Ignore if DWM attribute is unsupported on older Windows versions
        }
    }

    /// <summary>
    /// Forces DWM to repaint the system title bar after the immersive-mode
    /// attribute changed.
    /// Proven on this machine (Win10): DWM updates its stored attribute
    /// (DwmGetWindowAttribute reads the new value) but does NOT repaint the
    /// visible caption on WM_NCCALCSIZE (SWP_FRAMECHANGED) or on
    /// RedrawWindow(RDW_FRAME) — both verified ineffective in isolation.
    /// DWM does repaint the caption on WM_NCACTIVATE transitions, so toggle it
    /// synchronously (no delay: the intermediate inactive state never presents).
    /// A manual resize works for the same reason — it forces a full frame
    /// recompute + repaint; this achieves the repaint without touching geometry.
    /// </summary>
    private static void RefreshWindowFrame(IntPtr hwnd)
    {
        try
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            SendMessage(hwnd, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
        }
        catch
        {
            // Ignore - title bar will update on next frame recalculation
        }
    }

    private void RegisterSystemThemeChange()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // Ignore if not supported
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
        {
            UpdateSystemTheme();
        }
    }

    private void UpdateSystemTheme()
    {
        try
        {
            var newIsDark = IsSystemDark();
            if (newIsDark != _isSystemDark)
            {
                _isSystemDark = newIsDark;
                if (_currentTheme == AppTheme.System)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ApplyTheme();
                    });
                }
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    private bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 0;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.System;
        public string OutputDirectory { get; set; } = string.Empty;
    }
}