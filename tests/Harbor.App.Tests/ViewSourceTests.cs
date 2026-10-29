using System.IO;
using System.Xml.Linq;

namespace Harbor.App.Tests;

/// <summary>Checks on the view sources for bugs that only show in the running WPF app.</summary>
public class ViewSourceTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Xaml", name);

    // SPEC FR-26: radio buttons without a GroupName share one group per panel, so a sign-in click cleared the storage choice.
    [Fact]
    public void Each_settings_option_group_has_its_own_radio_group()
    {
        var radios = XDocument.Load(Source("SettingsWindow.xaml")).Descendants(Wpf + "RadioButton").ToList();

        Assert.All(radios, r => Assert.False(string.IsNullOrEmpty((string?)r.Attribute("GroupName")), (string?)r.Attribute("Content")));
        var groups = radios.GroupBy(r => (string)r.Attribute("GroupName")!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["Storage"] = 2, ["KeyProtection"] = 3, ["SignIn"] = 3 }, groups);
    }

    // Closing the main window: Close() inside the Closing event throws
    // "Cannot ... Close ... while a Window is closing"; the second close must be posted to the dispatcher.
    [Fact]
    public void Main_window_does_not_close_itself_from_inside_the_closing_event()
    {
        var code = File.ReadAllText(Source("MainWindow.xaml.cs"));
        var onClosing = code[code.IndexOf("OnClosing(", StringComparison.Ordinal)..];

        Assert.Contains("Dispatcher.BeginInvoke(Close)", onClosing, StringComparison.Ordinal);
        Assert.DoesNotContain("        Close();", onClosing, StringComparison.Ordinal);
    }
}
