namespace Ets2DiscordPresence;

internal static class JournalQuery
{
    internal sealed record Filter(string Search, DateTime? From, DateTime? To, bool Favorites,
        int Result, string? Truck, int Range, bool Miles, string? Tag, string Sort, bool Descending);
    internal sealed record ResultSet(TripEntry[] Entries, int Delivered, int Cancelled, double Km,
        double Seconds, string? TopRoute);

    internal static ResultSet Execute(IReadOnlyList<TripEntry> source, Filter filter, CancellationToken cancellation)
    {
        var matches = new List<TripEntry>();
        for (var index = 0; index < source.Count; index++)
        {
            if ((index & 127) == 0) cancellation.ThrowIfCancellationRequested();
            var entry = source[index];
            if (entry.Finished is not { } finish
                || filter.From is { } from && finish.LocalDateTime.Date < from
                || filter.To is { } to && finish.LocalDateTime.Date > to
                || filter.Favorites && !entry.Favorite
                || !Matches(entry, filter.Result, filter.Truck, filter.Range, filter.Miles, filter.Tag)) continue;
            bool Contains(string? value) => value?.Contains(filter.Search, StringComparison.CurrentCultureIgnoreCase) == true;
            if (filter.Search.Length > 0 && !(Contains(entry.Origin) || Contains(entry.Destination)
                || Contains(entry.Cargo) || Contains(entry.Tags) || Contains(entry.Notes))) continue;
            matches.Add(entry);
        }
        cancellation.ThrowIfCancellationRequested();
        var sorted = Sort(matches, filter.Sort, filter.Descending).ToArray();
        cancellation.ThrowIfCancellationRequested();
        var delivered = sorted.Where(entry => entry.Result == "delivered").ToArray();
        return new(sorted, delivered.Length, sorted.Count(entry => entry.Result == "cancelled"),
            sorted.Sum(entry => entry.Km), sorted.Sum(entry => entry.DrivingSeconds),
            delivered.GroupBy(entry => $"{entry.Origin} → {entry.Destination}")
                .OrderByDescending(group => group.Count()).FirstOrDefault()?.Key);
    }

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
            "result" => entry.Result, "km" => entry.Km, "time" => entry.DrivingSeconds, "revenue" => entry.Revenue ?? long.MinValue,
            _ => entry.Finished ?? DateTimeOffset.MinValue
        };
        return descending ? entries.OrderByDescending(Key) : entries.OrderBy(Key);
    }
}
