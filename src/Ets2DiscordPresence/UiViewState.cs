using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record JournalViewState(string Search = "", bool Favorites = false, DateTime? From = null,
    DateTime? To = null, int Result = 0, string? Truck = null, int Range = 0, string? Tag = null,
    string Sort = "date", bool Descending = true, Guid? Selected = null, int Scroll = 0, bool Filters = false);
internal sealed record ReplayViewState(string Search = "", bool Favorites = false, string? Selected = null, int Scroll = 0);

// Small view preferences are separate from settings, so cancelling Settings cannot discard them.
internal sealed class UiViewState(string path)
{
    private readonly Dictionary<string, JsonElement> values = Load(path);
    private Task writing = Task.CompletedTask;
    internal T? Get<T>(string key)
    {
        try { return values.TryGetValue(key, out var value) ? value.Deserialize<T>() : default; }
        catch (JsonException) { return default; }
    }
    internal void Set<T>(string key, T value)
    {
        values[key] = JsonSerializer.SerializeToElement(value);
    }
    internal Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(values);
        writing = writing.ContinueWith(_ =>
        {
            try { LocalData.WriteFile(path, json); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return writing;
    }
    private static Dictionary<string, JsonElement> Load(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length <= 64_000
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
