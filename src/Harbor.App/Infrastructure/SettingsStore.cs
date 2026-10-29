using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Core;
using Harbor.Windows.Security;

namespace Harbor.App.Infrastructure;

public static class SettingsStore
{
    public static HarborSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                using var stream = File.OpenRead(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.HarborSettings) ?? new HarborSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Settings could not be read; using defaults", ex);
        }

        return new HarborSettings();
    }

    public static void Save(HarborSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var tmp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.HarborSettings));
        File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
    }

    public static string? GetRemoteConnectionString(HarborSettings settings) =>
        string.IsNullOrEmpty(settings.RemoteConnectionStringProtected)
            ? null
            : DpapiKeyProtector.UnprotectString(settings.RemoteConnectionStringProtected);

    public static void SetRemoteConnectionString(HarborSettings settings, string? value) =>
        settings.RemoteConnectionStringProtected = string.IsNullOrWhiteSpace(value) ? null : DpapiKeyProtector.ProtectString(value.Trim());
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HarborSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
