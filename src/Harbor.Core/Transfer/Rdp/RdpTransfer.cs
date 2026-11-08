using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harbor.Core.Transfer.Rdp;

/// <summary>
/// What the in-session helper copies (SPEC FR-63, ADR-0041), written by the app as <c>plan.json</c> into a folder the
/// server reaches through the shared PC drive (<c>\\tsclient\C\…</c>).
/// </summary>
public sealed class RdpTransferPlan
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Guid? ConnectionId { get; set; }
    public bool IsUpload { get; set; } = true;

    /// <summary>Folder on the server (environment variables such as <c>%USERPROFILE%</c> are expanded there).</summary>
    public string ServerFolder { get; set; } = "";

    /// <summary>Download target on the PC as the server sees it (<c>\\tsclient\D\Downloads</c>).</summary>
    public string ClientFolder { get; set; } = "";

    /// <summary>Download target on the PC, as shown in the app.</summary>
    public string LocalFolder { get; set; } = "";

    public int ChunkSize { get; set; } = TransferJob.DefaultChunkSize;
    public bool Overwrite { get; set; }

    /// <summary>Leave files that already exist at the destination as they are (SPEC FR-64); ignored with <see cref="Overwrite"/>.</summary>
    public bool SkipExisting { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Upload: the files (sources as <c>\\tsclient</c> paths, destinations relative to <see cref="ServerFolder"/>).
    /// Download: empty — the files are picked in the session.
    /// </summary>
    public List<RdpTransferItem> Items { get; set; } = [];
}

public sealed record RdpTransferItem(string Source, string Destination);

/// <summary>Progress the helper writes as <c>status.json</c> after every chunk; read by the app.</summary>
public sealed class RdpTransferStatus
{
    /// <summary><c>running</c>, <c>waiting</c> (connection lost), <c>done</c>, <c>failed</c> or <c>cancelled</c>.</summary>
    public string State { get; set; } = "";
    public int FilesDone { get; set; }
    public int FilesTotal { get; set; }

    /// <summary>Files left alone because they already existed (SPEC FR-64); counted in <see cref="FilesDone"/> too.</summary>
    public int FilesSkipped { get; set; }
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public string? CurrentFile { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonIgnore]
    public bool IsFinished => State is "done" or "failed" or "cancelled";
}

/// <summary>A plan written to disk, with what the user runs in the session to start it.</summary>
public sealed record RdpTransferPrepared(RdpTransferPlan Plan, string PlanFolder, string StartCommand, IReadOnlyList<char> Drives);

/// <summary>
/// Prepares copies that go through the Remote Desktop connection itself, for networks where only the Remote Desktop
/// port is open (SPEC FR-63, ADR-0041). It writes the copy helper (<c>harbor-copy.ps1</c>), a plan and a <c>start.cmd</c>
/// into <c>rdp-transfers</c> in the data folder; the app then connects with the drives the plan needs shared into the
/// session, and the user starts <see cref="RdpTransferPrepared.StartCommand"/> there (Win+R). The helper reports back
/// through <c>status.json</c>; a <c>cancel</c> file asks it to stop.
/// </summary>
/// <param name="directory">The <c>rdp-transfers</c> folder in the data folder.</param>
/// <param name="clientPath">Maps a local path to the server's view of it; <see cref="ToClientPath"/> unless testing.</param>
public sealed class RdpTransferKit(string directory, Func<string, string>? clientPath = null)
{
    private readonly Func<string, string> _clientPath = clientPath ?? ToClientPath;

    public const string ScriptName = "harbor-copy.ps1";
    private const string ClientRoot = @"\\tsclient\";

    public string Directory => directory;

    /// <summary>Bytes between two checkpoints of the helper's journal.</summary>
    public int ChunkSize { get; init; } = TransferJob.DefaultChunkSize;

    /// <summary>The helper script shipped with the app.</summary>
    public static string Script
    {
        get
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Harbor.Core.Transfer.Rdp.harbor-copy.ps1")
                ?? throw new InvalidOperationException("The transfer helper is missing from the build.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// <c>C:\data\a.iso</c> as the server sees it through the shared drive: <c>\\tsclient\C\data\a.iso</c>. Only paths on
    /// a drive letter can be shared into a session.
    /// </summary>
    public static string ToClientPath(string localPath)
    {
        var full = DriveOf(localPath) is not null ? localPath : Path.GetFullPath(localPath);
        if (DriveOf(full) is not { } drive)
        {
            throw new ArgumentException($"{localPath} is not on a drive letter, so it cannot be shared into Remote Desktop.", nameof(localPath));
        }

        return ClientRoot + drive + full[2..].Replace('/', '\\');
    }

    /// <summary>The drive letter of a Windows path (<c>C</c> for <c>C:\x</c>), or <c>null</c>.</summary>
    public static char? DriveOf(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ? char.ToUpperInvariant(path[0]) : null;

    /// <summary>
    /// The <c>drivestoredirect</c> line that shares <paramref name="drives"/> (plus any drives the entry already shares)
    /// for this connection. <c>*</c> in the entry's settings already shares all drives.
    /// </summary>
    public static string DriveRedirection(string? entrySettings, IEnumerable<char> drives)
    {
        var current = entrySettings?.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("drivestoredirect:s:", StringComparison.OrdinalIgnoreCase))?["drivestoredirect:s:".Length..] ?? "";
        if (current.Split(';').Any(d => d.Trim() == "*")) return "drivestoredirect:s:*";

        var all = new List<string>();
        foreach (var part in current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Concat(drives.Select(d => $"{d}:\\")))
        {
            if (!all.Contains(part, StringComparer.OrdinalIgnoreCase)) all.Add(part);
        }

        return "drivestoredirect:s:" + string.Join(";", all) + ";";
    }

    /// <summary>Plan to copy local <paramref name="files"/> (source, path relative to the server folder) to <paramref name="serverFolder"/>.</summary>
    public RdpTransferPrepared CreateUpload(string label, Guid? connectionId, IReadOnlyList<(string Source, string RelativeDestination)> files,
        string serverFolder, ExistingFiles existing)
    {
        var plan = NewPlan(label, connectionId, existing);
        plan.IsUpload = true;
        plan.ServerFolder = serverFolder;
        plan.Items = files.Select(f => new RdpTransferItem(_clientPath(f.Source), f.RelativeDestination.Replace('/', '\\'))).ToList();
        return Write(plan, files.Select(f => f.Source));
    }

    /// <summary>Plan to copy files picked in the session (starting in <paramref name="serverFolder"/>) to local <paramref name="localFolder"/>.</summary>
    public RdpTransferPrepared CreateDownload(string label, Guid? connectionId, string serverFolder, string localFolder, ExistingFiles existing)
    {
        var plan = NewPlan(label, connectionId, existing);
        plan.IsUpload = false;
        plan.ServerFolder = serverFolder;
        plan.ClientFolder = _clientPath(localFolder);
        plan.LocalFolder = localFolder;
        return Write(plan, [localFolder]);
    }

    private RdpTransferPlan NewPlan(string label, Guid? connectionId, ExistingFiles existing) => new()
    {
        ChunkSize = ChunkSize,
        // Short id: the start command typed in the session (Win+R) must stay well under 260 characters.
        Id = Guid.NewGuid().ToString("N")[..12],
        Label = label,
        ConnectionId = connectionId,
        Overwrite = existing == ExistingFiles.Replace,
        SkipExisting = existing == ExistingFiles.Skip,
    };

    private RdpTransferPrepared Write(RdpTransferPlan plan, IEnumerable<string> localPaths)
    {
        var folder = Path.Combine(directory, plan.Id);
        System.IO.Directory.CreateDirectory(folder);
        var scriptPath = Path.Combine(directory, ScriptName);
        File.WriteAllText(scriptPath, Script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var planPath = Path.Combine(folder, "plan.json");
        File.WriteAllBytes(planPath, JsonSerializer.SerializeToUtf8Bytes(plan, RdpTransferJsonContext.Default.RdpTransferPlan));
        var startPath = Path.Combine(folder, "start.cmd");
        File.WriteAllText(startPath, StartScript(_clientPath(scriptPath), _clientPath(planPath)), new UTF8Encoding(false));

        // The data folder's drive is shared too: the helper, plan and status live there.
        var drives = localPaths.Append(directory).Select(DriveOf).OfType<char>().Distinct().Order().ToList();
        return new RdpTransferPrepared(plan, folder, _clientPath(startPath), drives);
    }

    /// <summary>
    /// <c>start.cmd</c>: runs the helper from the shared drive. The script is loaded as a script block, so an execution
    /// policy that blocks unsigned scripts from network paths does not stop it.
    /// </summary>
    internal static string StartScript(string scriptPath, string planPath)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''").Replace("%", "%%") + "'";
        return "@echo off\r\nchcp 65001 >nul\r\ntitle Remote Desktop Harbor - file transfer\r\n" +
               "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"& ([scriptblock]::Create([IO.File]::ReadAllText(" +
               Quote(scriptPath) + "))) -Plan " + Quote(planPath) + "\"\r\n";
    }

    /// <summary>All plans in the folder, oldest first.</summary>
    public IReadOnlyList<RdpTransferPlan> List()
    {
        if (!System.IO.Directory.Exists(directory)) return [];
        var plans = new List<RdpTransferPlan>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "plan.json", SearchOption.AllDirectories))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllBytes(file), RdpTransferJsonContext.Default.RdpTransferPlan) is { } plan) plans.Add(plan);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return plans.OrderBy(p => p.CreatedUtc).ToList();
    }

    /// <summary>The helper's last report, or <c>null</c> before it started.</summary>
    public RdpTransferStatus? ReadStatus(string planId)
    {
        var path = Path.Combine(directory, planId, "status.json");
        try
        {
            if (!File.Exists(path)) return null;
            var status = JsonSerializer.Deserialize(File.ReadAllBytes(path), RdpTransferJsonContext.Default.RdpTransferStatus);
            if (status is not null && string.IsNullOrEmpty(status.Error)) status.Error = null;
            return status;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // being replaced right now; read again on the next tick
        }
    }

    public string StartCommand(string planId) => _clientPath(Path.Combine(directory, planId, "start.cmd"));

    /// <summary>Asks a running helper to stop and delete its partial files.</summary>
    public void Cancel(string planId) => File.WriteAllText(Path.Combine(directory, planId, "cancel"), "");

    public void Delete(string planId)
    {
        var folder = Path.Combine(directory, planId);
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(RdpTransferPlan))]
[JsonSerializable(typeof(RdpTransferStatus))]
internal sealed partial class RdpTransferJsonContext : JsonSerializerContext;
