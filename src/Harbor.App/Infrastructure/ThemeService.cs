using System.Windows;
using Harbor.Core;
using Wpf.Ui.Appearance;

namespace Harbor.App.Infrastructure;

/// <summary>Applies the chosen colour theme to the WPF UI theme dictionaries. SPEC FR-08, ADR-0020</summary>
public static class ThemeService
{
    private static AppTheme _theme;
    private static Window? _window;

    public static void Apply(AppTheme theme)
    {
        _theme = theme;
        if (theme == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
        }
        else
        {
            ApplicationThemeManager.Apply(theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }

        UpdateWatcher();
    }

    /// <summary>Follows Windows light/dark changes through this (loaded) window while the theme is System.</summary>
    public static void Follow(Window window)
    {
        _window = window;
        UpdateWatcher();
    }

    private static void UpdateWatcher()
    {
        if (_window is not { IsLoaded: true } window) return;
        SystemThemeWatcher.UnWatch(window);
        if (_theme == AppTheme.System) SystemThemeWatcher.Watch(window);
    }
}
