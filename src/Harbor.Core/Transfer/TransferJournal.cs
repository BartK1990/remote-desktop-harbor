using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harbor.Core.Transfer;

/// <summary>
/// Keeps every transfer's state as <c>&lt;id&gt;.json</c> in a folder of this PC's data directory, replaced atomically
/// after each chunk, so the app always knows where a copy stopped — across dropped connections, restarts and crashes.
/// SPEC FR-61, ADR-0039
/// </summary>
public sealed class TransferJournal(string directory)
{
    public string Directory => directory;

    public void Save(TransferJob job)
    {
        System.IO.Directory.CreateDirectory(directory);
        var path = PathFor(job.Id);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(job, TransferJsonContext.Default.TransferJob));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>All journaled jobs, oldest first. Unreadable files are skipped (and reported).</summary>
    public IReadOnlyList<TransferJob> LoadAll(Action<string, Exception>? onUnreadable = null)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var jobs = new List<TransferJob>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllBytes(file), TransferJsonContext.Default.TransferJob) is { } job)
                {
                    jobs.Add(job);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                onUnreadable?.Invoke(file, ex);
            }
        }

        return jobs.OrderBy(j => j.CreatedUtc).ToList();
    }

    private string PathFor(Guid id) => Path.Combine(directory, $"{id:N}.json");
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TransferJob))]
internal sealed partial class TransferJsonContext : JsonSerializerContext;
