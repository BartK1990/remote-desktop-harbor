using System.Windows;
using Harbor.Core;
using Harbor.Windows.Theming;
using Microsoft.Win32;

namespace Harbor.App.Infrastructure;

/// <summary>
/// Applies the chosen colour theme: the WPF Fluent theme (<see cref="Application.ThemeMode"/>) and Harbor's own
/// light or dark colour tokens (<c>Themes/Colors.*.xaml</c>). While <see cref="AppTheme.System"/> is chosen, a
/// Windows light/dark switch swaps the tokens live, as the Fluent theme does. SPEC FR-08, ADR-0025
/// </summary>
public static class ThemeService
{
    private static readonly Uri LightColors = new("/Themes/Colors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkColors = new("/Themes/Colors.Dark.xaml", UriKind.Relative);

    private static AppTheme _current;
    private static bool _watching;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;
        _current = theme;
        app.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        ApplyColors(app);

        if (!_watching)
        {
            _watching = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // The app mode lives under "Personalize", announced as a General preference change.
        if (e.Category != UserPreferenceCategory.General || _current != AppTheme.System) return;
        if (Application.Current is { } app) app.Dispatcher.BeginInvoke(() => ApplyColors(app));
    }

    private static void ApplyColors(Application app)
    {
        var source = _current.IsDark(WindowsAppMode.IsDark()) ? DarkColors : LightColors;
        var dictionaries = app.Resources.MergedDictionaries;
        var index = dictionaries.ToList().FindIndex(IsHarborColors);
        if (index >= 0 && dictionaries[index].Source == source) return;

        var colors = new ResourceDictionary { Source = source };
        if (index >= 0) dictionaries[index] = colors;
        else dictionaries.Add(colors);
    }

    private static bool IsHarborColors(ResourceDictionary d) =>
        d.Source?.OriginalString.StartsWith("/Themes/Colors.", StringComparison.OrdinalIgnoreCase) == true;
}
