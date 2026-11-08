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

    public static void SetPortable(bool enabled)
    {
        var marker = Path.Combine(Portable, "portable");
        if (enabled)
        {
            Directory.CreateDirectory(Portable);
            File.WriteAllText(marker, "");
        }
        else File.Delete(marker);
    }

    public static string JournalPath(string profile) => Path.Combine(Folder, profile.Length == 0 ? "trips.json"
        : $"trips-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))}.json");

    public static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public static void Backup(string file, string? folder = null)
    {
        folder ??= Folder;
        var files = new Dictionary<string, string>();
        if (Directory.Exists(folder))
        {
            var catalogs = Path.Combine(folder, "catalogs");
            long inputBytes = 0;
            foreach (var path in Directory.EnumerateFiles(folder, "*.json")
                .Concat(Directory.Exists(catalogs) ? Directory.EnumerateFiles(catalogs, "*.json") : []))
            {
                var name = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if (FileKind(name) == "") continue;
                inputBytes += new FileInfo(path).Length;
                if (inputBytes > BackupLimit) throw new InvalidDataException("Backup too large.");
                files.Add(name, File.ReadAllText(path));
            }
        }
        var document = JsonSerializer.Serialize(files);
        if (Encoding.UTF8.GetByteCount(document) > BackupLimit) throw new InvalidDataException("Backup too large.");
        WriteFile(Path.GetFullPath(file), document);
    }

    public static void Restore(string file, string? folder = null)
    {
        folder ??= Folder;
        if (new FileInfo(file).Length > BackupLimit) throw new InvalidDataException("Backup too large.");
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("Empty backup.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in files)
        {
            if (content is null || !unique.Add(name)) throw new InvalidDataException("Invalid backup entry.");
            switch (FileKind(name))
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
                default:
                    throw new InvalidDataException("Unknown backup entry.");
            }
        }

        var replaced = new Dictionary<string, string?>();
        try
        {
            foreach (var (name, content) in files)
            {
                var path = Path.Combine(folder, name);
                var previous = File.Exists(path) ? File.ReadAllText(path) : null;
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

    private static string FileKind(string name)
    {
        if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return "settings";
        if (name == "trips.json" || System.Text.RegularExpressions.Regex.IsMatch(name, @"^trips-[0-9A-F]{64}\.json\z")) return "trips";
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
        foreach (var file in Directory.EnumerateFiles(RecordingTrash, "ETS2-*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) >= threshold) yield return file;
            else File.Delete(file);
        }
    }

    public static IEnumerable<string> RecordingFiles() => Directory.Exists(Recordings)
        ? Directory.EnumerateFiles(Recordings, "ETS2-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc) : [];

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
