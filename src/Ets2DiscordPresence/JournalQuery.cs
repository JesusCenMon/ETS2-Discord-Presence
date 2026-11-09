namespace Ets2DiscordPresence;

internal static class JournalQuery
{
    public static string[] Tags(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(TripEntry entry, int result, string? truck, int range, bool miles, string? tag)
    {
        if (result == 1 && entry.Result != "delivered" || result == 2 && entry.Result != "cancelled") return false;
        if (truck is not null && !string.Equals(truck, entry.Truck, StringComparison.CurrentCultureIgnoreCase)) return false;
        if (tag is not null && !Tags(entry.Tags).Contains(tag, StringComparer.CurrentCultureIgnoreCase)) return false;
        var distance = miles ? entry.Km / 1.609344 : entry.Km;
        return range switch
        {
            1 => distance < 100, 2 => distance is >= 100 and < 500, 3 => distance is >= 500 and < 1000,
            4 => distance >= 1000, _ => true
        };
    }

    public static IEnumerable<TripEntry> Sort(IEnumerable<TripEntry> entries, string column, bool descending)
    {
        IComparable Key(TripEntry entry) => column switch
        {
            "origin" => entry.Origin, "destination" => entry.Destination, "cargo" => entry.Cargo,
            "result" => entry.Result, "km" => entry.Km, "time" => entry.DrivingSeconds,
            _ => entry.Finished ?? DateTimeOffset.MinValue
        };
        return descending ? entries.OrderByDescending(Key) : entries.OrderBy(Key);
    }
}
