using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Harbor.App.ViewModels;
using Harbor.App.Views;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Transfer;
using Harbor.Core.Transfer.Rdp;

namespace Harbor.App.Tests;

// A WPF Application is global to the process: the window tests run alone and leave none behind, so tests of code that
// looks at Application.Current (e.g. the theme) still see no application.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfWindowCollection
{
    public const string Name = "WPF windows";
}

// SPEC FR-60, FR-63: the real File transfer window, driven like a user would (WPF on an STA thread; Windows only).
[Collection(WpfWindowCollection.Name)]
public sealed class TransfersWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"harbor-transfers-window-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private class NoDialogs : IDialogService
    {
        public bool EditEntry(EntryEditorViewModel editor) => false;
        public bool EditSettings(SettingsViewModel settings) => false;
        public bool EditSmartGroup(SmartGroupEditorViewModel editor) => false;
        public bool Confirm(string title, string message) => false;
        public bool ConfirmRisky(string title, string message, string confirmText) => false;
        public void ShowError(string title, string message) { }
        public string? PickRdpFile() => null;
        public string? PickRdmCsvFile() => null;
        public string? PickBackupFileToSave(string suggestedName) => null;
        public string? PickBackupFileToOpen() => null;
        public string? AskBackupPassphrase(bool isNew) => null;
        public string[]? PickFilesToUpload() => null;
        public string[]? PickFilesToDownload(string remoteFolder) => null;
        public virtual string? PickFolder(string title, string? initialFolder) => null;
        public void ShowTransfers(TransfersViewModel transfers) { }
    }

    private sealed class FolderPicker(string? answer) : NoDialogs
    {
        public string? Started { get; private set; }

        public override string? PickFolder(string title, string? initialFolder)
        {
            Started = initialFolder;
            return answer;
        }
    }

    private sealed class NoSessions : IRdpTransferSessionHost
    {
        public bool IsAvailable => true;
        public IRdpTransferSession Open(RdpTransferSessionSettings settings, string? password, RdpTransferSessionRequest request) =>
            throw new InvalidOperationException("not in tests");
    }

    // Pack URIs such as /Themes/Tokens.xaml resolve against the application's resource assembly: the entry assembly,
    // which in a test run is the test host. Its public setter refuses whenever there is an entry assembly, so the test
    // points WPF's two copies of it at the app by reflection (test only; the app is its own entry assembly).
    static TransfersWindowTests()
    {
        var app = typeof(App).Assembly;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var field = typeof(Application).GetFields(Static).FirstOrDefault(f => f.FieldType == typeof(Assembly) && f.Name.Contains("resourceAssembly", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("WPF's resource assembly field was not found");
        field.SetValue(null, app);
        var helper = new[] { typeof(UIElement).Assembly, typeof(Application).Assembly, typeof(DependencyObject).Assembly }
                         .SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == "BaseUriHelper")
                     ?? throw new InvalidOperationException("BaseUriHelper was not found");
        var property = helper.GetProperty("ResourceAssembly", Static) ?? throw new InvalidOperationException("BaseUriHelper.ResourceAssembly was not found");
        property.SetValue(null, app);
    }

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                new App().InitializeComponent(); // the app's resources, no startup
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ForgetApplication();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>Clears WPF's current application (test only), so the next test can create its own and others see none.</summary>
    private static void ForgetApplication()
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var field in typeof(Application).GetFields(Static))
        {
            if (field.FieldType == typeof(Application)) field.SetValue(null, null);
            else if (field.FieldType == typeof(bool) && field.Name.Contains("appCreated", StringComparison.OrdinalIgnoreCase)) field.SetValue(null, false);
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }

    /// <summary>What a mouse click does: focus moves to the button, then its Click (and toggle) runs.</summary>
    private static void Click(ButtonBase button)
    {
        button.Focus();
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
        Pump();
    }

    private TransfersViewModel NewViewModel(IDialogService? dialogs = null, Func<TransferJob, CancellationToken, Task>? connect = null,
        HarborSettings? settings = null) => new(
        new TransferQueue(new TransferJournal(Path.Combine(_dir, "journal"))), new RdpTransferKit(Path.Combine(_dir, "rdp")), new NoSessions(),
        dialogs ?? new NoDialogs(), connect ?? ((_, _) => Task.CompletedTask), (_, _, _, _) => Task.FromResult(false), settings ?? new HarborSettings(), () => { });

    // SPEC FR-60: Browse… connects the share with the entry's account, then picks the folder starting at the one in the
    // box, or at the default one when the box is empty; the share root when that folder is not there.
    [Fact]
    public void Browse_connects_the_share_and_starts_in_the_folder_on_the_host() => RunOnSta(() =>
    {
        var picker = new FolderPicker(@"\\srv\C$\Data\In");
        var connected = new List<string>();
        var settings = new HarborSettings();
        var vm = NewViewModel(picker, (job, _) => { connected.Add(job.SourcePath); return Task.CompletedTask; }, settings);
        vm.Target(ConnectionEntry.CreateNew("srv") with { Host = "srv-does-not-exist.invalid" }, null);
        vm.UseNetworkShare = true;
        vm.RemoteFolder = "";

        vm.BrowseRemoteFolderCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal([@"\\srv-does-not-exist.invalid\C$\Users\Public\Downloads"], connected); // the default, connected first
        Assert.Equal(@"\\srv-does-not-exist.invalid\C$", picker.Started); // not there: starts at the share
        Assert.Equal(@"\\srv\C$\Data\In", vm.RemoteFolder);
        Assert.Equal(@"\\srv\C$\Data\In", settings.TransferRemoteFolder);
    });

    // The bug report: with Network share chosen and its folder left empty, Remote Desktop could not be chosen again.
    [Fact]
    public void Copy_over_can_go_back_to_remote_desktop_also_with_an_empty_folder() => RunOnSta(() =>
    {
        var vm = NewViewModel();
        vm.Target(ConnectionEntry.CreateNew("srv") with { Host = "srv" }, null);

        for (var round = 0; round < 2; round++) // the app reuses the view model when the window is opened again
        {
            var window = new TransfersWindow(vm);
            window.Show();
            Pump();
            var method = Find<RadioButton>(window).Where(r => r.GroupName == "Method").ToList();
            var (remoteDesktop, share) = (method[0], method[1]);
            var folderBox = Find<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Folder on the host");

            Click(share);
            Assert.True(vm.UseNetworkShare, $"round {round}: Network share chosen");
            Assert.True(share.IsChecked);

            folderBox.Focus();
            folderBox.Clear(); // left empty
            Pump();
            Assert.Equal("", vm.RemoteFolder);

            Click(remoteDesktop);
            Assert.True(vm.UseRemoteDesktop, $"round {round}: Remote Desktop chosen again (view model)");
            Assert.True(remoteDesktop.IsChecked, $"round {round}: Remote Desktop chosen again (button)");
            Assert.False(share.IsChecked);

            Click(share);
            Assert.True(vm.UseNetworkShare, $"round {round}: Network share chosen again");
            Click(remoteDesktop);
            Assert.True(vm.UseRemoteDesktop, $"round {round}: and Remote Desktop again");

            window.Close();
            Pump();
        }
    });
}
