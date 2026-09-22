using System;
using System.Windows;
using System.Windows.Media;

namespace PDF2CBZ.Controls;

public class CustomWindow : Window
{
    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(CustomWindow), new PropertyMetadata(36.0));

    public static readonly DependencyProperty WindowTitleProperty =
        DependencyProperty.Register(nameof(WindowTitle), typeof(string), typeof(CustomWindow), new PropertyMetadata("PDF2CBZ"));

    public static readonly DependencyProperty WindowIconProperty =
        DependencyProperty.Register(nameof(WindowIcon), typeof(ImageSource), typeof(CustomWindow), new PropertyMetadata(null));

    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    public string WindowTitle
    {
        get => (string)GetValue(WindowTitleProperty);
        set => SetValue(WindowTitleProperty, value);
    }

    public ImageSource? WindowIcon
    {
        get => (ImageSource?)GetValue(WindowIconProperty);
        set => SetValue(WindowIconProperty, value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Earliest moment the HWND exists — apply DWM immersive mode before
        // the first non-client frame is drawn, so startup shows the right
        // system title bar without requiring a resize.
        Themes.ThemeManager.ApplyThemeToWindow(this, Themes.ThemeManager.Instance.IsDark);
    }
}