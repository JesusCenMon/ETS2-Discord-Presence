using System.Drawing.Drawing2D;

namespace Ets2DiscordPresence;

internal sealed class TripMap : Control
{
    private readonly List<(double X, double Z, bool Break)> trail = [];
    private IReadOnlyList<MapCity> cities = [];
    private MapCity? destination;
    private TelemetrySnapshot? truck;
    private bool interrupted = true;
    private bool english;
    private bool light;
    private string? tripKey;

    public TripMap()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 220;
        Dock = DockStyle.Top;
        AccessibleName = "Mapa del viaje / Trip map";
    }

    public void UpdateMap(TelemetrySnapshot? snapshot, CityLookup catalog, bool isEnglish, bool isLight, bool demo)
    {
        english = isEnglish;
        light = isLight;
        cities = catalog.Cities;
        var key = demo ? "demo" : snapshot?.OnJob == true ? snapshot.Signals?.JobKey + snapshot.Signals?.Deadline : "free";
        if (snapshot is not null && key != tripKey)
        {
            trail.Clear();
            interrupted = true;
            tripKey = key;
        }

        truck = snapshot is { } value && double.IsFinite(value.X) && double.IsFinite(value.Z)
            && Math.Abs(value.X) <= 2_000_000 && Math.Abs(value.Z) <= 2_000_000 ? value : null;
        destination = truck?.OnJob == true ? catalog.FindDestination(truck.DestinationCity, truck.Signals?.DestinationId) : null;
        if (truck is null) interrupted = true;
        else if (!truck.Paused && !demo)
        {
            var last = trail.LastOrDefault();
            var dx = truck.X - last.X;
            var dz = truck.Z - last.Z;
            var change = dx * dx + dz * dz;
            if (trail.Count == 0 || interrupted || change >= 100)
            {
                trail.Add((truck.X, truck.Z, interrupted || change > 16_000_000));
                interrupted = false;
                if (trail.Count > 4000) trail.RemoveAt(0);
            }
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var background = light ? Color.FromArgb(228, 238, 242) : Color.FromArgb(14, 26, 34);
        var label = light ? Color.FromArgb(32, 57, 68) : Color.FromArgb(218, 231, 234);
        var accent = light ? Color.FromArgb(0, 104, 91) : Color.FromArgb(128, 229, 201);
        var finish = light ? Color.FromArgb(143, 74, 0) : Color.FromArgb(245, 192, 117);
        g.Clear(background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(45, label));
        for (var x = 0; x < Width; x += 28) g.DrawLine(grid, x, 0, x, Height);
        for (var y = 0; y < Height; y += 28) g.DrawLine(grid, 0, y, Width, y);
        var caption = english ? "Approximate trip map" : "Mapa aproximado del viaje";
        TextRenderer.DrawText(g, caption, Font, new Rectangle(10, 4, Width - 20, 24), label, TextFormatFlags.EndEllipsis);
        if (truck is null)
        {
            TextRenderer.DrawText(g, english ? "Waiting for the truck's position" : "Esperando la posición del camión",
                Font, ClientRectangle, label, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var left = Math.Min(truck.X - 11_000, destination?.X ?? truck.X);
        var right = Math.Max(truck.X + 11_000, destination?.X ?? truck.X);
        var top = Math.Min(truck.Z - 11_000, destination?.Z ?? truck.Z);
        var bottom = Math.Max(truck.Z + 11_000, destination?.Z ?? truck.Z);
        var scale = Math.Min(Math.Max(1, Width - 80) / (right - left), Math.Max(1, Height - 80) / (bottom - top));
        PointF Place(double x, double z) => new((float)(Width / 2d + (x - (left + right) / 2) * scale),
            (float)(Height / 2d + 10 + (z - (top + bottom) / 2) * scale));

        using var track = new Pen(accent, 2);
        for (var i = 1; i < trail.Count; i++)
            if (!trail[i].Break) g.DrawLine(track, Place(trail[i - 1].X, trail[i - 1].Z), Place(trail[i].X, trail[i].Z));
        if (trail.Count > 1)
            TextRenderer.DrawText(g, english ? "Green · Recorded track" : "Verde · Recorrido registrado",
                Font, new Rectangle(10, Height - 22, Width - 20, 20), accent, TextFormatFlags.EndEllipsis);

        var occupied = new List<Rectangle>();
        void Marker(string name, PointF point, Color color, int size)
        {
            if (point.X < 8 || point.X > Width - 8 || point.Y < 30 || point.Y > Height - 10) return;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, point.X - size / 2f, point.Y - size / 2f, size, size);
            var measured = TextRenderer.MeasureText(name, Font);
            var box = new Rectangle(Math.Clamp((int)point.X + 7, 2, Math.Max(2, Width - measured.Width)),
                Math.Max(26, (int)point.Y - measured.Height), measured.Width, measured.Height);
            if (occupied.Any(other => other.IntersectsWith(box))) return;
            using var backing = new SolidBrush(background);
            g.FillRectangle(backing, box);
            TextRenderer.DrawText(g, name, Font, box, color);
            occupied.Add(box);
        }

        Marker(english ? "Truck" : "Camión", Place(truck.X, truck.Z), accent, 10);
        if (destination is { } target) Marker(truck.DestinationCity, Place(target.X, target.Z), finish, 10);
        foreach (var city in cities.Where(city => city.X >= left && city.X <= right && city.Z >= top && city.Z <= bottom)
            .OrderBy(city => Math.Abs(truck.X - city.X) + Math.Abs(truck.Z - city.Z)).Take(12))
            if (city != destination) Marker(city.Name, Place(city.X, city.Z), label, 4);
    }
}
