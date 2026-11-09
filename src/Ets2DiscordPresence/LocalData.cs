using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal static class LocalData
{
    private const int BackupLimit = 50_000_000;
    private static readonly string Standard = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETS2DiscordPresence");
    private static readonly string Portable = Path.Combine(AppContext.BaseDirectory, "ETS2PresenceData");
    public static bool IsPortable { get; } = File.Exists(Path.Combine(Portable, "portable"));
    public static string Folder { get; } = IsPortable ? Portable : Standard;
    public static string Recordings => Path.Combine(Folder, "recordings");

    public static void SetPortable(bool enabled, bool copyData = false)
    {
        var marker = Path.Combine(Portable, "portable");
        if (copyData && enabled != IsPortable)
        {
            var temporary = Path.GetTempFileName();
            try
            {
                Backup(temporary);
                Restore(temporary, enabled ? Portable : Standard);
            }
            finally { File.Delete(temporary); }
        }
        if (enabled)
        {
            Directory.CreateDirectory(Portable);
            File.WriteAllText(marker, "");
        }
        else File.Delete(marker);
    }

    public static string JournalPath(string profile) => Path.Combine(Folder, profile.Length == 0 ? "trips.json"
        : $"trips-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))}.json");

    public static void WriteFile(string path, string content) => WriteFile(path, Encoding.UTF8.GetBytes(content));

    private static void WriteFile(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private sealed record BackupEntry(string Sha256, string Content);
    private sealed record BackupManifest(int SchemaVersion, DateTimeOffset Created, string Application,
        string AppVersion, Dictionary<string, BackupEntry> Files);

    public static long RecordingBytes(string? active) =>
        RecordingFiles().Where(path => !string.Equals(path, active, StringComparison.OrdinalIgnoreCase))
            .Sum(path => new FileInfo(path).Length);

    public static void Backup(string file, string? folder = null, bool includeRecordings = false, string? active = null)
    {
        folder ??= Folder;
        var files = new Dictionary<string, BackupEntry>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            var catalogs = Path.Combine(folder, "catalogs");
            long inputBytes = 0;
            var recordings = Path.Combine(folder, "recordings");
            foreach (var path in Directory.EnumerateFiles(folder, "*.json")
                .Concat(Directory.Exists(catalogs) ? Directory.EnumerateFiles(catalogs, "*.json") : [])
                .Concat(includeRecordings && Directory.Exists(recordings)
                    ? Directory.EnumerateFiles(recordings, "ETS2-*.jsonl*").Where(path => !string.Equals(path, active, StringComparison.OrdinalIgnoreCase))
                    : []))
            {
                var name = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (FileKind(name) == "") continue;
                inputBytes += new FileInfo(path).Length;
                if (inputBytes > BackupLimit) throw new InvalidDataException("Backup too large.");
                var bytes = File.ReadAllBytes(path);
                files.Add(name, new BackupEntry(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes)));
            }
        }
        var document = JsonSerializer.Serialize(new BackupManifest(1, DateTimeOffset.UtcNow, "ETS2DiscordPresence",
            DesktopServices.Version.ToString(3), files));
        if (Encoding.UTF8.GetByteCount(document) > BackupLimit) throw new InvalidDataException("Backup too large.");
        WriteFile(Path.GetFullPath(file), document);
    }

    public static void Restore(string file, string? folder = null)
    {
        folder ??= Folder;
        if (new FileInfo(file).Length > BackupLimit) throw new InvalidDataException("Backup too large.");
        using var archive = JsonDocument.Parse(File.ReadAllText(file));
        var root = archive.RootElement;
        EnsureUniqueProperties(root);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var versioned = root.TryGetProperty("SchemaVersion", out _);
        if (versioned && (root.GetProperty("SchemaVersion").GetInt32() != 1
            || root.GetProperty("Application").GetString() != "ETS2DiscordPresence"
            || !root.GetProperty("Created").TryGetDateTimeOffset(out _)
            || !Version.TryParse(root.GetProperty("AppVersion").GetString(), out _)))
            throw new InvalidDataException("Unsupported backup manifest.");
        var entries = versioned ? root.GetProperty("Files") : root;
        foreach (var item in entries.EnumerateObject())
        {
            var name = item.Name;
            if (versioned)
            {
                EnsureUniqueProperties(item.Value);
                var entry = item.Value.Deserialize<BackupEntry>() ?? throw new InvalidDataException("Invalid backup entry.");
                if (entry.Content is null) throw new InvalidDataException("Missing backup data.");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(entry.Content); }
                catch (FormatException ex) { throw new InvalidDataException("Invalid backup data.", ex); }
                if (!string.Equals(entry.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)
                    || !files.TryAdd(name, bytes))
                    throw new InvalidDataException("Duplicate entry or mismatched checksum.");
            }
            else if (item.Value.GetString() is not { } value || !files.TryAdd(name, Encoding.UTF8.GetBytes(value)))
                throw new InvalidDataException("Invalid backup entry.");
        }
        foreach (var (name, bytes) in files)
        {
            var kind = FileKind(name);
            var content = kind == "recording" ? "" : Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            switch (kind)
            {
                case "settings":
                    var settings = JsonSerializer.Deserialize<AppSettings>(content);
                    if (settings is null || settings.DriverProfile is null || settings.DriverProfile != settings.DriverProfile.Trim()) throw new InvalidDataException("Invalid settings.");
                    break;
                case "trips":
                    var journal = JsonSerializer.Deserialize<JournalData>(content);
                    if (journal?.Entries is null || journal.Pending is null
                        || journal.Entries.Concat(journal.Pending).Concat(journal.Trash ?? []).Any(entry => entry is null || entry.Cargo is null
                            || entry.Origin is null || entry.Destination is null || entry.Km is < 0 or > 1_000_000
                            || entry.DrivingSeconds is < 0 or > 100_000_000))
                        throw new InvalidDataException("Invalid journal.");
                    break;
                case "catalog":
                    if (Encoding.UTF8.GetByteCount(content) > 5_000_000) throw new InvalidDataException("Invalid catalog.");
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content))) _ = new CityLookup(stream);
                    break;
                case "recording" when versioned:
                    using (var recording = new MemoryStream(bytes)) _ = TelemetryReplay.ReadFile(recording, name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    throw new InvalidDataException("Unknown backup entry.");
            }
        }

        var replaced = new Dictionary<string, byte[]?>();
        try
        {
            foreach (var (name, content) in files)
            {
                var path = Path.Combine(folder, name);
                var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
                WriteFile(path, content);
                replaced.Add(path, previous);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (path, previous) in replaced)
            {
                if (previous is null) File.Delete(path);
                else WriteFile(path, previous);
            }
            throw;
        }
    }

    private static void EnsureUniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid backup object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate backup property.");
    }

    private static string FileKind(string name)
    {
        if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return "settings";
        if (name == "trips.json" || System.Text.RegularExpressions.Regex.IsMatch(name, @"^trips-[0-9A-F]{64}\.json\z")) return "trips";
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^recordings/ETS2-[a-zA-Z0-9_.-]+\.jsonl(\.gz)?\z")) return "recording";
        if (!name.StartsWith("catalogs/", StringComparison.Ordinal)) return "";
        var filename = name[9..];
        return filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !filename.Contains('/')
            && !filename.Contains('\\') && filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? "catalog" : "";
    }

    public static string RecordingTrash => Path.Combine(Recordings, "trash");

    public static void TrashRecording(string path)
    {
        Directory.CreateDirectory(RecordingTrash);
        var destination = Path.Combine(RecordingTrash, Path.GetFileName(path));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        File.Move(path, destination);
    }

    public static void RestoreRecording(string path)
    {
        var name = Path.GetFileName(path);
        File.Move(path, Path.Combine(Recordings, name));
    }

    public static IEnumerable<string> TrashedRecordings()
    {
        if (!Directory.Exists(RecordingTrash)) yield break;
        var threshold = DateTime.UtcNow.AddDays(-30);
        foreach (var file in Directory.EnumerateFiles(RecordingTrash, "ETS2-*.jsonl*").Where(IsRecordingPath))
        {
            if (File.GetLastWriteTimeUtc(file) >= threshold) yield return file;
            else File.Delete(file);
        }
    }

    public static IEnumerable<string> RecordingFiles() => Directory.Exists(Recordings)
        ? Directory.EnumerateFiles(Recordings, "ETS2-*.jsonl*").Where(IsRecordingPath).OrderByDescending(File.GetLastWriteTimeUtc) : [];

    public static string? FindRecording(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) return null;
        var path = Path.Combine(Recordings, name);
        return File.Exists(path) ? path : File.Exists(path + ".gz") ? path + ".gz" : null;
    }

    private static bool IsRecordingPath(string path) => path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    public static void PruneRecordings(int days, string? active)
    {
        foreach (var _ in TrashedRecordings()) { }
        if (days <= 0) return;
        var threshold = DateTime.UtcNow.AddDays(-days);
        foreach (var file in RecordingFiles())
            if (!string.Equals(file, active, StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(file) < threshold)
                TrashRecording(file);
    }
}
