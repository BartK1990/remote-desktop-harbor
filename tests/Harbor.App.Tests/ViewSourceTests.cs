using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Harbor.App.Tests;

/// <summary>Checks on the view sources for bugs that only show in the running WPF app.</summary>
public class ViewSourceTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Xaml", name);

    // SPEC FR-26: radio buttons without a GroupName share one group per panel, so a sign-in click cleared the storage choice.
    [Fact]
    public void Each_settings_option_group_has_its_own_radio_group()
    {
        var radios = XDocument.Load(Source("SettingsWindow.xaml")).Descendants(Wpf + "RadioButton").ToList();

        Assert.All(radios, r => Assert.False(string.IsNullOrEmpty((string?)r.Attribute("GroupName")), (string?)r.Attribute("Content")));
        var groups = radios.GroupBy(r => (string)r.Attribute("GroupName")!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["Storage"] = 2, ["KeyProtection"] = 3, ["SignIn"] = 3, ["Theme"] = 3 }, groups);
    }

    // SPEC FR-45: settings sit on tabs; each tab scrolls on its own and keeps a right gutter so the overlay
    // scroll bar of the Fluent theme does not cover the input fields.
    [Fact]
    public void Settings_tabs_scroll_with_a_gutter_for_the_scroll_bar()
    {
        var doc = XDocument.Load(Source("SettingsWindow.xaml"));
        var tabs = doc.Descendants(Wpf + "TabItem").Select(t => (string)t.Attribute("Header")!).ToList();
        Assert.Equal(new[] { "Storage", "Shared vault", "Sign-in", "Remote Desktop", "Appearance", "About" }, tabs);

        AssertTabsKeepGutter(doc);
    }

    // SPEC FR-46, ADR-0024: the entry editor sits on tabs like Settings; the RDP tabs only show for an RDP connection,
    // and every tab keeps the scroll bar gutter.
    [Fact]
    public void Entry_editor_tabs_scroll_with_a_gutter_for_the_scroll_bar()
    {
        var doc = XDocument.Load(Source("EntryEditorWindow.xaml"));
        var tabs = doc.Descendants(Wpf + "TabItem").ToList();
        Assert.Equal(new[] { "General", "Accounts", "Display", "Local resources", "Connection" },
            tabs.Select(t => (string)t.Attribute("Header")!));
        Assert.Equal(new[] { "Display", "Local resources", "Connection" },
            tabs.Where(t => (string?)t.Attribute("Style") == "{StaticResource RdpTab}").Select(t => (string)t.Attribute("Header")!));

        AssertTabsKeepGutter(doc);
    }

    private static void AssertTabsKeepGutter(XDocument doc)
    {
        Assert.All(doc.Descendants(Wpf + "TabItem"), tab =>
        {
            var scroller = Assert.Single(tab.Elements(Wpf + "ScrollViewer"));
            Assert.Equal("{StaticResource TabScroller}", (string?)scroller.Attribute("Style"));
            Assert.Equal("{StaticResource TabPage}", (string?)scroller.Elements().Single().Attribute("Style"));
        });
        var page = doc.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == "TabPage");
        var margin = (string)page.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "Margin").Attribute("Value")!;
        Assert.True(int.Parse(margin.Split(',')[2]) >= 16, margin);
    }

    // SPEC FR-08, ADR-0025: views take colours from the Fluent theme or Harbor's tokens, never fixed values, so both
    // themes stay readable.
    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("EntryEditorWindow.xaml")]
    [InlineData("SettingsWindow.xaml")]
    [InlineData("PassphraseWindow.xaml")]
    [InlineData("TitleBar.xaml")]
    [InlineData("MessageWindow.xaml")]
    public void Views_do_not_hard_code_colours(string view)
    {
        var colourProperties = new[] { "Foreground", "Background", "BorderBrush", "Fill", "Stroke" };
        var elements = XDocument.Load(Source(view)).Descendants().ToList();
        var values = elements.SelectMany(e => e.Attributes()).Where(a => colourProperties.Contains(a.Name.LocalName)).Select(a => a.Value)
            .Concat(elements.Where(e => e.Name.LocalName == "Setter" && colourProperties.Contains((string?)e.Attribute("Property")))
                .Select(e => (string?)e.Attribute("Value") ?? "{element}"));
        var fixedColours = values.Where(v => !v.StartsWith('{') && v != "Transparent").ToList();

        Assert.Empty(fixedColours);
    }

    // ADR-0025: the light and dark colour sets define the same keys, and every Harbor token a view uses exists.
    [Fact]
    public void Light_and_dark_colour_tokens_match_and_cover_the_views()
    {
        static HashSet<string> Keys(string file) => XDocument.Load(Source(Path.Combine("Themes", file))).Root!.Elements()
            .Select(e => (string)e.Attribute(Xaml + "Key")!).ToHashSet();
        var light = Keys("Colors.Light.xaml");
        var dark = Keys("Colors.Dark.xaml");
        Assert.Equal(light.Order(), dark.Order());

        var defined = light.Concat(Keys("Tokens.xaml")).Concat(Keys("Controls.xaml")).ToHashSet();
        var used = Directory.GetFiles(Path.GetDirectoryName(Source("MainWindow.xaml"))!, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Resource (Harbor\.[\w.]+)\}").Select(m => m.Groups[1].Value))
            .ToHashSet();
        Assert.NotEmpty(used);
        Assert.Empty(used.Except(defined));
    }

    // SPEC FR-47, FR-48, FR-50, ADR-0026, ADR-0028: the main window binds its shortcuts, and every shortcut shown on a button is bound.
    [Fact]
    public void Main_window_binds_the_shortcuts_it_shows()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var bound = doc.Root!.Element(Wpf + "Window.InputBindings")!.Elements(Wpf + "KeyBinding").ToDictionary(
            k => string.Join("+", new[] { (string?)k.Attribute("Modifiers"), (string)k.Attribute("Key")! }.Where(p => !string.IsNullOrEmpty(p))),
            k => (string)k.Attribute("Command")!);

        foreach (var (gesture, command) in new Dictionary<string, string>
                 {
                     ["Ctrl+F"] = "FocusSearchCommand", ["Ctrl+K"] = "OpenQuickConnectCommand", ["Ctrl+D"] = "TogglePinCommand", ["Ctrl+N"] = "AddCommand", ["F5"] = "RefreshCommand",
                     ["F2"] = "EditCommand", ["Ctrl+Shift+C"] = "CopyPasswordCommand", ["Ctrl+Shift+U"] = "CopyUserNameCommand",
                     ["Ctrl+Shift+H"] = "CopyHostCommand", ["Ctrl+OemComma"] = "OpenSettingsCommand",
                 })
        {
            Assert.True(bound.TryGetValue(gesture, out var binding), gesture);
            Assert.Contains(command, binding, StringComparison.Ordinal);
        }

        var shown = doc.Descendants(Wpf + "TextBlock")
            .Where(t => (string?)t.Attribute("Style") == "{StaticResource Harbor.Text.KeyHint}")
            .Select(t => (string)t.Attribute("Text")!);
        Assert.All(shown, hint => Assert.Contains(hint, bound.Keys));
    }

    // SPEC FR-08, FR-45, FR-46, ADR-0027: both dialogs use the side navigation and save with Ctrl+S; the theme is
    // chosen in Settings, not in the main window.
    [Theory]
    [InlineData("SettingsWindow.xaml")]
    [InlineData("EntryEditorWindow.xaml")]
    public void Dialogs_use_the_side_navigation_and_save_with_ctrl_s(string view)
    {
        var doc = XDocument.Load(Source(view));
        Assert.Equal("{StaticResource Harbor.NavTabControl}", (string?)Assert.Single(doc.Descendants(Wpf + "TabControl")).Attribute("Style"));
        var save = Assert.Single(doc.Root!.Element(Wpf + "Window.InputBindings")!.Elements(Wpf + "KeyBinding"));
        Assert.Equal(("S", "Ctrl", "ApplicationCommands.Save"),
            ((string?)save.Attribute("Key"), (string?)save.Attribute("Modifiers"), (string?)save.Attribute("Command")));
    }

    [Fact]
    public void Theme_is_chosen_in_settings_not_in_the_main_window()
    {
        Assert.DoesNotContain(XDocument.Load(Source("MainWindow.xaml")).Descendants(Wpf + "RadioButton"),
            r => (string?)r.Attribute("GroupName") == "Theme");
        Assert.Equal(3, XDocument.Load(Source("SettingsWindow.xaml")).Descendants(Wpf + "RadioButton")
            .Count(r => (string?)r.Attribute("GroupName") == "Theme"));
    }

    // SPEC FR-52, ADR-0029: every window draws the app's 32 px title bar over a WindowChrome frame; dialogs show Close only.
    [Theory]
    [InlineData("MainWindow.xaml", true)]
    [InlineData("EntryEditorWindow.xaml", false)]
    [InlineData("SettingsWindow.xaml", false)]
    [InlineData("PassphraseWindow.xaml", false)]
    [InlineData("MessageWindow.xaml", false)]
    public void Windows_use_the_app_title_bar(string view, bool minimizeMaximize)
    {
        XNamespace views = "clr-namespace:Harbor.App.Views";
        var doc = XDocument.Load(Source(view));
        var chrome = Assert.Single(doc.Root!.Element(Wpf + "WindowChrome.WindowChrome")!.Elements(Wpf + "WindowChrome"));
        Assert.Equal("32", (string?)chrome.Attribute("CaptionHeight"));
        Assert.Equal("False", (string?)chrome.Attribute("UseAeroCaptionButtons"));
        // -1 (frame extended over the whole window) lets Windows draw its own caption buttons under ours.
        Assert.Equal("0,0,0,1", (string?)chrome.Attribute("GlassFrameThickness"));
        Assert.Equal("{DynamicResource Harbor.WindowBackgroundBrush}", (string?)doc.Root.Attribute("Background"));
        var bar = Assert.Single(doc.Descendants(views + "TitleBar"));
        Assert.Equal(minimizeMaximize ? null : "False", (string?)bar.Attribute("ShowMinimizeMaximize"));
    }

    // SPEC FR-53, ADR-0030: confirmations and errors use the app's message box, not the system one.
    [Fact]
    public void Main_window_uses_the_app_message_box()
    {
        var code = File.ReadAllText(Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("MessageBox.Show(", code, StringComparison.Ordinal);
        Assert.Contains("MessageWindow.Show(", code, StringComparison.Ordinal);
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
