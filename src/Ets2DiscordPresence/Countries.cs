namespace Ets2DiscordPresence;

internal static class Countries
{
    private static readonly Dictionary<string, (string Es, string En)> Names = new(StringComparer.Ordinal)
    {
        ["AL"] = ("Albania", "Albania"), ["AT"] = ("Austria", "Austria"),
        ["BA"] = ("Bosnia y Herzegovina", "Bosnia and Herzegovina"), ["BE"] = ("Bélgica", "Belgium"),
        ["BG"] = ("Bulgaria", "Bulgaria"), ["CH"] = ("Suiza", "Switzerland"), ["CZ"] = ("Chequia", "Czechia"),
        ["DE"] = ("Alemania", "Germany"), ["DK"] = ("Dinamarca", "Denmark"), ["EE"] = ("Estonia", "Estonia"),
        ["ES"] = ("España", "Spain"), ["FI"] = ("Finlandia", "Finland"), ["FR"] = ("Francia", "France"),
        ["GB"] = ("el Reino Unido", "the United Kingdom"), ["GR"] = ("Grecia", "Greece"),
        ["HR"] = ("Croacia", "Croatia"), ["HU"] = ("Hungría", "Hungary"), ["IE"] = ("Irlanda", "Ireland"),
        ["IS"] = ("Islandia", "Iceland"), ["IT"] = ("Italia", "Italy"), ["LT"] = ("Lituania", "Lithuania"),
        ["LU"] = ("Luxemburgo", "Luxembourg"), ["LV"] = ("Letonia", "Latvia"),
        ["ME"] = ("Montenegro", "Montenegro"), ["MK"] = ("Macedonia del Norte", "North Macedonia"),
        ["NL"] = ("los Países Bajos", "the Netherlands"), ["NO"] = ("Noruega", "Norway"),
        ["PL"] = ("Polonia", "Poland"), ["PT"] = ("Portugal", "Portugal"), ["RO"] = ("Rumanía", "Romania"),
        ["RS"] = ("Serbia", "Serbia"), ["RU"] = ("Rusia", "Russia"), ["SE"] = ("Suecia", "Sweden"),
        ["SI"] = ("Eslovenia", "Slovenia"), ["SK"] = ("Eslovaquia", "Slovakia"), ["TR"] = ("Turquía", "Türkiye"),
        ["UA"] = ("Ucrania", "Ukraine"), ["XK"] = ("Kosovo", "Kosovo")
    };

    public static string? Normalize(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        return value is not null && Names.ContainsKey(value) ? value : null;
    }

    public static string? Name(string? code, bool english) =>
        code is not null && Names.TryGetValue(code, out var name) ? english ? name.En : name.Es : null;
}
