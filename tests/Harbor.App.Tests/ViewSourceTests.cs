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

    // SPEC FR-02: the entry tree rows keep a right gutter, so the overlay scroll bar of the Fluent theme does not cover
    // the host or the in-use marker on the right of a row.
    [Fact]
    public void Entry_tree_rows_keep_a_gutter_for_the_scroll_bar()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var tree = doc.Descendants(Wpf + "TreeView").Single(t => (string?)t.Attribute(Xaml + "Name") == "EntryTree");
        var header = tree.Descendants(Wpf + "ContentPresenter").Single(c => (string?)c.Attribute(Xaml + "Name") == "PART_Header");
        var margin = (string)header.Attribute("Margin")!;
        Assert.True(int.Parse(margin.Split(',')[2]) >= 16, margin);
    }

    // SPEC FR-55, ADR-0034: the entry list has smaller / reset / larger buttons, and the zoom scales each row and the
    // indent of its children, not the tree (whose scroll bar keeps its size).
    [Fact]
    public void Entry_tree_rows_and_indent_follow_the_zoom_with_a_reset_button()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var commands = doc.Descendants(Wpf + "Button").Select(b => (string?)b.Attribute("Command")).ToList();
        Assert.Contains("{Binding ZoomTreeOutCommand}", commands);
        Assert.Contains("{Binding ResetTreeZoomCommand}", commands);
        Assert.Contains("{Binding ZoomTreeInCommand}", commands);

        var tree = doc.Descendants(Wpf + "TreeView").Single(t => (string?)t.Attribute(Xaml + "Name") == "EntryTree");
        Assert.Null(tree.Element(Wpf + "TreeView.LayoutTransform"));
        var scaled = tree.Descendants(Wpf + "ScaleTransform").ToList();
        Assert.Equal(2, scaled.Count);
        Assert.All(scaled, t => Assert.Contains("DataContext.TreeZoom", (string)t.Attribute("ScaleX")!, StringComparison.Ordinal));
        Assert.Contains("DataContext.TreeZoom", (string?)scaled[0].Attribute("ScaleY") ?? "", StringComparison.Ordinal);
    }

    // SPEC FR-56: the list and details columns are named so the window can restore the remembered split, and a
    // double-click on the splitter goes back to the default width.
    [Fact]
    public void List_pane_width_is_restored_and_can_be_reset()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var columns = doc.Descendants(Wpf + "ColumnDefinition").Select(c => (string?)c.Attribute(Xaml + "Name")).ToList();
        Assert.Contains("ListColumn", columns);
        Assert.Contains("DetailsColumn", columns);
        var splitter = Assert.Single(doc.Descendants(Wpf + "GridSplitter"));
        Assert.Equal("PaneSplitter_MouseDoubleClick", (string?)splitter.Attribute("MouseDoubleClick"));
    }

    // SPEC FR-29, ADR-0038: the entry editor offers "Shared database" / "This PC only" as its own radio group, and the
    // details pane and the tree context menu can move the selected entry both ways.
    [Fact]
    public void Entries_can_be_stored_on_this_pc_and_moved_both_ways()
    {
        var editor = XDocument.Load(Source("EntryEditorWindow.xaml"));
        Assert.Equal(new[] { "Shared database", "This PC only" }, editor.Descendants(Wpf + "RadioButton")
            .Where(r => (string?)r.Attribute("GroupName") == "Storage").Select(r => (string)r.Attribute("Content")!));

        var commands = XDocument.Load(Source("MainWindow.xaml")).Descendants(Wpf + "MenuItem").Select(m => (string?)m.Attribute("Command")).ToList();
        Assert.Equal(2, commands.Count(c => c == "{Binding MoveToLocalCommand}"));
        Assert.Equal(2, commands.Count(c => c == "{Binding MoveToSharedCommand}"));
        Assert.Equal(2, commands.Count(c => c == "{Binding TransferFilesCommand}"));
    }

    // SPEC FR-60: the transfer list keeps a right gutter so the scroll bar never covers the Pause / Resume / Cancel buttons.
    [Fact]
    public void Transfer_rows_keep_a_gutter_for_the_scroll_bar()
    {
        var list = Assert.Single(XDocument.Load(Source("TransfersWindow.xaml")).Descendants(Wpf + "ListBox"));
        var padding = (string)list.Descendants(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "Padding").Attribute("Value")!;
        Assert.True(int.Parse(padding.Split(',')[2]) >= 16, padding);
    }

    // SPEC FR-62, ADR-0040: the tree has rows for smart groups and the entries they list, and its context menu manages
    // them; the editor saves with Ctrl+S like the other dialogs.
    [Fact]
    public void Tree_shows_smart_groups_and_its_menu_manages_them()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var types = doc.Descendants().Where(e => e.Name.LocalName is "HierarchicalDataTemplate" or "DataTemplate")
            .Select(t => (string?)t.Attribute("DataType")).ToList();
        Assert.Contains("{x:Type vm:SmartGroupViewModel}", types);
        Assert.Contains("{x:Type vm:EntryLinkViewModel}", types);

        var tree = doc.Descendants(Wpf + "TreeView").Single(t => (string?)t.Attribute(Xaml + "Name") == "EntryTree");
        var commands = tree.Descendants(Wpf + "MenuItem").Select(m => (string?)m.Attribute("Command")).ToList();
        Assert.Contains("{Binding NewSmartGroupCommand}", commands);
        Assert.Contains("{Binding EditSmartGroupCommand}", commands);
        Assert.Contains("{Binding DeleteSmartGroupCommand}", commands);

        var editor = XDocument.Load(Source("SmartGroupEditorWindow.xaml"));
        var save = Assert.Single(editor.Root!.Element(Wpf + "Window.InputBindings")!.Elements(Wpf + "KeyBinding"));
        Assert.Equal("ApplicationCommands.Save", (string?)save.Attribute("Command"));
    }

    // SPEC FR-48: the Quick connect results keep a right gutter too, so the scroll bar does not cover the in-use marker.
    [Fact]
    public void Quick_connect_results_keep_a_gutter_for_the_scroll_bar()
    {
        var doc = XDocument.Load(Source("MainWindow.xaml"));
        var list = doc.Descendants(Wpf + "ListBox").Single(l => (string?)l.Attribute(Xaml + "Name") == "QuickList");
        var padding = (string)list.Descendants(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "Padding").Attribute("Value")!;
        Assert.True(int.Parse(padding.Split(',')[2]) >= 16, padding);
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
    [InlineData("TransfersWindow.xaml")]
    [InlineData("SmartGroupEditorWindow.xaml")]
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

    // SPEC FR-47, FR-48, FR-50, FR-55, ADR-0026, ADR-0028: the main window binds its shortcuts, and every shortcut shown on a button is bound.
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
                     ["Ctrl+OemPlus"] = "ZoomTreeInCommand", ["Ctrl+OemMinus"] = "ZoomTreeOutCommand", ["Ctrl+D0"] = "ResetTreeZoomCommand",
                     ["Ctrl+Shift+T"] = "TransferFilesCommand",
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
    [InlineData("TransfersWindow.xaml", false)]
    [InlineData("SmartGroupEditorWindow.xaml", false)]
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
