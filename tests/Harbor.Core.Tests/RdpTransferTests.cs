using System.Diagnostics;
using System.Text.Json;
using Harbor.Core.Transfer;
using Harbor.Core.Transfer.Rdp;

namespace Harbor.Core.Tests;

// SPEC FR-63, ADR-0041: copying through the Remote Desktop connection with the in-session helper.
public sealed class RdpTransferTests : IDisposable
{
    private const int Chunk = 64 * 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"harbor-rdp-{Guid.NewGuid():N}");

    public RdpTransferTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(@"C:\data\a.iso", @"\\tsclient\C\data\a.iso")]
    [InlineData(@"d:\Users\bak\AppData\Local\RemoteDesktopHarbor\rdp-transfers\x\start.cmd", @"\\tsclient\D\Users\bak\AppData\Local\RemoteDesktopHarbor\rdp-transfers\x\start.cmd")]
    public void Local_paths_map_to_the_shared_drive(string local, string client) => Assert.Equal(client, RdpTransferKit.ToClientPath(local));

    [Fact]
    public void Paths_without_a_drive_letter_cannot_be_shared() =>
        Assert.Throws<ArgumentException>(() => RdpTransferKit.ToClientPath(@"\\fileserver\share\a.txt"));

    [Theory]
    [InlineData(null, @"drivestoredirect:s:C:\;")]
    [InlineData("audiomode:i:0\r\ndrivestoredirect:s:E:\\;", @"drivestoredirect:s:E:\;C:\;")]
    [InlineData(@"drivestoredirect:s:c:\;", @"drivestoredirect:s:c:\;")]
    [InlineData("drivestoredirect:s:*", "drivestoredirect:s:*")]
    public void The_connection_shares_the_needed_drives_and_keeps_the_entrys_own(string? settings, string expected) =>
        Assert.Equal(expected, RdpTransferKit.DriveRedirection(settings, ['C']));

    [Fact]
    public void Launch_overrides_win_over_the_entry_settings_for_this_launch_only()
    {
        var entry = new Harbor.Core.Model.ConnectionEntry
        {
            Id = Guid.NewGuid(), Name = "srv", Host = "srv", RdpSettings = "drivestoredirect:s:E:\\;\r\naudiomode:i:2",
        };
        var line = RdpTransferKit.DriveRedirection(entry.RdpSettings, ['C']);
        var rdp = Harbor.Core.Rdp.RdpFile.Build(entry, overrides: [line, "full address:s:evil"]);

        Assert.Contains("drivestoredirect:s:E:\\;C:\\;\r\n", rdp, StringComparison.Ordinal);
        Assert.DoesNotContain("drivestoredirect:s:E:\\;\r\n", rdp, StringComparison.Ordinal);
        Assert.Contains("audiomode:i:2", rdp, StringComparison.Ordinal);
        Assert.Contains("full address:s:srv", rdp, StringComparison.Ordinal); // managed keys cannot be overridden
        Assert.Equal(1, rdp.Split("drivestoredirect").Length - 1);
    }

    [Fact]
    public void The_start_command_loads_the_helper_without_the_execution_policy_and_quotes_paths()
    {
        var cmd = RdpTransferKit.StartScript(@"\\tsclient\C\Users\o'brien\h.ps1", @"\\tsclient\C\100%\plan.json");
        Assert.Contains("[scriptblock]::Create([IO.File]::ReadAllText('\\\\tsclient\\C\\Users\\o''brien\\h.ps1'))", cmd, StringComparison.Ordinal);
        Assert.Contains("-Plan '\\\\tsclient\\C\\100%%\\plan.json'", cmd, StringComparison.Ordinal);
        Assert.StartsWith("@echo off", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_holds_no_secret_and_names_the_drives_to_share()
    {
        var kit = new RdpTransferKit(@"C:\Harbor\rdp-transfers");
        Assert.Equal(@"\\tsclient\C\Harbor\rdp-transfers\abc\start.cmd", kit.StartCommand("abc"));
        Assert.False(string.IsNullOrWhiteSpace(RdpTransferKit.Script));
        Assert.DoesNotContain("password", RdpTransferKit.Script.Split('\n').Where(l => !l.TrimStart().StartsWith('#')), StringComparer.OrdinalIgnoreCase);
    }

    // SPEC FR-65, ADR-0042: Harbor's transfer window types the start command with /auto, so the helper's window closes at the end.
    [Fact]
    public void The_transfer_window_starts_the_helper_without_the_final_pause()
    {
        var kit = new RdpTransferKit(@"C:\Harbor\rdp-transfers");
        Assert.Equal(@"\\tsclient\C\Harbor\rdp-transfers\abc\start.cmd /auto", kit.AutoStartCommand("abc"));
        var cmd = RdpTransferKit.StartScript(@"\\tsclient\C\h.ps1", @"\\tsclient\C\plan.json");
        Assert.Contains("if /i \"%~1\"==\"/auto\" set \"HARBOR_ARGS=-NoPause\"", cmd, StringComparison.Ordinal);
        Assert.Contains("-Plan '\\\\tsclient\\C\\plan.json' %HARBOR_ARGS%\"", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_remembers_the_account_to_connect_with()
    {
        var account = Guid.NewGuid();
        var prepared = Kit.CreateUpload("SRV01", Guid.NewGuid(), [(File("a.txt", 1), "a.txt")], ServerFolder, ExistingFiles.Fail, account);
        Assert.Equal(account, Assert.Single(Kit.List()).AccountId);
        Assert.Equal(account, prepared.Plan.AccountId);
    }

    // ADR-0042: 1 MiB chunks, so a dropped slow link loses little and progress moves often.
    [Fact]
    public void Chunks_are_one_mebibyte()
    {
        Assert.Equal(1024 * 1024, TransferJob.DefaultChunkSize);
        Assert.Equal(1024 * 1024, new RdpTransferKit(_dir).ChunkSize);
        Assert.Contains("$ChunkSize = 1MB", RdpTransferKit.Script, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the helper itself (needs PowerShell)

    private RdpTransferKit Kit => new(Path.Combine(_dir, "pc", "rdp-transfers"), p => p) { ChunkSize = Chunk }; // no \\tsclient off Windows

    private string File(string name, int length, int seed = 1)
    {
        var path = Path.Combine(_dir, "pc", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    private string ServerFolder => Path.Combine(_dir, "server", "in");

    [Fact]
    public void Upload_survives_a_dropped_connection()
    {
        if (PowerShell is null) return;
        var big = File("big.bin", 5 * Chunk + 123);
        var small = File("small.txt", 10, seed: 2);
        var prepared = Upload([(big, "big.bin"), (small, "sub/small.txt")]);

        Assert.Equal(0, Run(prepared, "-TestFailAfterChunks 2"));

        Assert.Equal(System.IO.File.ReadAllBytes(big), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "big.bin")));
        Assert.Equal(System.IO.File.ReadAllBytes(small), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "sub", "small.txt")));
        var status = Kit.ReadStatus(prepared.Plan.Id)!;
        Assert.Equal(("done", 2, 2), (status.State, status.FilesDone, status.FilesTotal));
        Assert.Null(status.Error);
        Assert.Empty(Directory.GetFiles(JournalFolder)); // the server journal is removed once done
    }

    [Fact]
    public void An_ended_session_resumes_from_the_server_journal()
    {
        if (PowerShell is null) return;
        var big = File("big.bin", 6 * Chunk);
        var prepared = Upload([(big, "big.bin")]);

        Assert.Equal(3, Run(prepared, "-TestExitAfterChunks 2")); // the helper was stopped mid-copy
        var journal = JsonDocument.Parse(System.IO.File.ReadAllText(Assert.Single(Directory.GetFiles(JournalFolder))));
        Assert.Equal(2L * Chunk, journal.RootElement.GetProperty("items")[0].GetProperty("bytesDone").GetInt64());
        Assert.Equal(2L * Chunk, new FileInfo(Path.Combine(ServerFolder, "big.bin.harbor-part")).Length);

        Assert.Equal(0, Run(prepared)); // started again
        Assert.Equal(System.IO.File.ReadAllBytes(big), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "big.bin")));
        Assert.False(System.IO.File.Exists(Path.Combine(ServerFolder, "big.bin.harbor-part")));
    }

    [Fact]
    public void Download_copies_server_files_to_the_shared_pc_folder()
    {
        if (PowerShell is null) return;
        Directory.CreateDirectory(ServerFolder);
        var onServer = Path.Combine(ServerFolder, "log.zip");
        System.IO.File.WriteAllBytes(onServer, new byte[3 * Chunk + 5]);
        var target = Path.Combine(_dir, "pc", "Downloads");
        var prepared = Kit.CreateDownload("SRV01", null, ServerFolder, target, ExistingFiles.Fail);
        // In the session the files are picked in a dialog; a test lists them in the plan.
        prepared.Plan.Items = [new RdpTransferItem(onServer, "log.zip")];
        System.IO.File.WriteAllText(Path.Combine(prepared.PlanFolder, "plan.json"), JsonSerializer.Serialize(prepared.Plan,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        Assert.Equal(0, Run(prepared, "-TestFailAfterChunks 1"));
        Assert.Equal(System.IO.File.ReadAllBytes(onServer), System.IO.File.ReadAllBytes(Path.Combine(target, "log.zip")));
        Assert.False(prepared.Plan.IsUpload);
    }

    [Fact]
    public void An_existing_file_fails_and_cancel_stops_the_helper()
    {
        if (PowerShell is null) return;
        var file = File("a.txt", 100);
        Directory.CreateDirectory(ServerFolder);
        System.IO.File.WriteAllText(Path.Combine(ServerFolder, "a.txt"), "keep me");
        var exists = Upload([(file, "a.txt")]);
        Assert.Equal(1, Run(exists));
        Assert.Equal("failed", Kit.ReadStatus(exists.Plan.Id)!.State);
        Assert.Contains("already exists", Kit.ReadStatus(exists.Plan.Id)!.Error, StringComparison.Ordinal);
        Assert.Equal("keep me", System.IO.File.ReadAllText(Path.Combine(ServerFolder, "a.txt")));

        var cancelled = Upload([(File("b.txt", 100), "b.txt")]);
        Kit.Cancel(cancelled.Plan.Id);
        Assert.Equal(2, Run(cancelled));
        Assert.Equal("cancelled", Kit.ReadStatus(cancelled.Plan.Id)!.State);
        Assert.False(System.IO.File.Exists(Path.Combine(ServerFolder, "b.txt")));
        Assert.Equal(2, Kit.List().Count);
    }

    // ADR-0042: the transfer window may start the helper again after a reconnect; a second one leaves the running one alone.
    [Fact]
    public void A_second_helper_for_the_same_transfer_does_not_run()
    {
        if (PowerShell is null) return;
        var file = File("a.bin", 3 * Chunk);
        var prepared = Upload([(file, "a.bin")]);
        var lockFolder = Path.Combine(JournalFolder, "running");
        Directory.CreateDirectory(lockFolder);
        using (new FileStream(Path.Combine(lockFolder, prepared.Plan.Id + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(4, Run(prepared)); // "already running"
            Assert.False(System.IO.File.Exists(Path.Combine(ServerFolder, "a.bin")));
        }

        Assert.Equal(0, Run(prepared));
        Assert.Equal(System.IO.File.ReadAllBytes(file), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "a.bin")));
        Assert.Empty(Directory.GetFiles(lockFolder)); // released when done
    }

    [Fact]
    public void Skip_copies_only_files_not_yet_in_the_folder_and_its_sub_folders()
    {
        // SPEC FR-64: a folder upload with sub-folders; files already on the server are left as they are.
        var top = File(Path.Combine("folder", "top.txt"), 100, seed: 1);
        var deep = File(Path.Combine("folder", "sub", "deep", "deep.bin"), 2 * Chunk + 3, seed: 2);
        var same = File(Path.Combine("folder", "sub", "same.txt"), 50, seed: 3);
        Directory.CreateDirectory(Path.Combine(ServerFolder, "folder", "sub"));
        System.IO.File.WriteAllText(Path.Combine(ServerFolder, "folder", "sub", "same.txt"), "keep me");
        System.IO.File.WriteAllText(Path.Combine(ServerFolder, "folder", "top.log"), "other extension"); // not the same file

        var prepared = Kit.CreateUpload("SRV01", null,
            [(top, @"folder\top.txt"), (deep, @"folder\sub\deep\deep.bin"), (same, @"folder\sub\same.txt")], ServerFolder, ExistingFiles.Skip);
        Assert.True(prepared.Plan.SkipExisting);
        Assert.False(prepared.Plan.Overwrite);
        if (PowerShell is null) return;

        Assert.Equal(0, Run(prepared));
        Assert.Equal(System.IO.File.ReadAllBytes(top), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "folder", "top.txt")));
        Assert.Equal(System.IO.File.ReadAllBytes(deep), System.IO.File.ReadAllBytes(Path.Combine(ServerFolder, "folder", "sub", "deep", "deep.bin")));
        Assert.Equal("keep me", System.IO.File.ReadAllText(Path.Combine(ServerFolder, "folder", "sub", "same.txt")));
        var status = Kit.ReadStatus(prepared.Plan.Id)!;
        Assert.Equal(("done", 3, 3, 1), (status.State, status.FilesDone, status.FilesTotal, status.FilesSkipped));
        Assert.Equal(status.BytesTotal, status.BytesDone);
    }

    private RdpTransferPrepared Upload(IReadOnlyList<(string, string)> files) => Kit.CreateUpload("SRV01", null, files, ServerFolder, ExistingFiles.Fail);

    private string JournalFolder => Path.Combine(_dir, "server-local", "RemoteDesktopHarbor", "transfers");

    /// <summary>Runs the helper as <c>start.cmd</c> does: loaded as a script block.</summary>
    private int Run(RdpTransferPrepared prepared, string extra = "")
    {
        var script = Path.Combine(Kit.Directory, RdpTransferKit.ScriptName).Replace("'", "''");
        var plan = Path.Combine(prepared.PlanFolder, "plan.json").Replace("'", "''");
        var info = new ProcessStartInfo(PowerShell!)
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command",
                $"& ([scriptblock]::Create([IO.File]::ReadAllText('{script}'))) -Plan '{plan}' -NoPause {extra}; exit $LASTEXITCODE" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["LOCALAPPDATA"] = Path.Combine(_dir, "server-local");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "The helper did not finish.");
        Assert.True(string.IsNullOrWhiteSpace(error), error + output.Result);
        return process.ExitCode;
    }

    /// <summary>Windows PowerShell on Windows (what servers run), otherwise PowerShell 7 when installed.</summary>
    private static readonly string? PowerShell = FindPowerShell();

    private static string? FindPowerShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, "pwsh"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "pwsh"));
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
