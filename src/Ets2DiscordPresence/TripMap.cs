using System.Drawing.Drawing2D;

namespace Ets2DiscordPresence;

internal sealed class TripMap : Control
{
    private readonly List<(double X, double Z, bool Break, float? Speed, string? Event)> trail = [];
    private IReadOnlyList<MapCity> cities = [];
    private MapCity? destination;
    private MapCity? origin;
    private MapCity? focusCity;
    private (double X, double Z)? fixedCenter;
    private bool fitTrail;
    private double bearing;
    private (TelemetrySnapshot Position, string Symbol)? highlightedReplayEvent;

    public void HighlightReplayEvent(TelemetrySnapshot? position, string? symbol)
    {
        highlightedReplayEvent = position is not null && symbol is not null ? (position, symbol) : null;
        Invalidate();
    }

    internal static string EventSymbol(string? kind) => kind switch
    {
        "pickup" => "↑", "delivery" => "✓", "fuel" => "+", "rest" => "Ⅱ", "ferry" => "≈",
        "route_changed" => "↻", string value when value.StartsWith("border:", StringComparison.Ordinal) => "⚑", _ => "◆"
    };
    private bool headingUp;
    private bool follow;
    private bool colorSpeed;
    private bool showCities = true;
    private bool showTrack = true;
    private bool showTerminals = true;
    private bool showEvents = true;

    public void ConfigureLayer(string key, bool enabled)
    {
        switch (key)
        {
            case "heading": headingUp = enabled; break;
            case "follow":
                follow = enabled;
                if (enabled) Recenter();
                break;
            case "cities": showCities = enabled; break;
            case "track": showTrack = enabled; break;
            case "terminals": showTerminals = enabled; break;
            case "events": showEvents = enabled; break;
            case "speed": colorSpeed = enabled; break;
        }
        Invalidate();
    }

    public void FitTrail()
    {
        Recenter();
        fitTrail = true;
        Invalidate();
    }

    public void FocusCity(MapCity city)
    {
        Recenter();
        focusCity = city;
        Invalidate();
    }

    private void SuspendFollowing()
    {
        if (truck is not null) fixedCenter ??= (truck.X, truck.Z);
    }

    private void AddPoint(TelemetrySnapshot point, bool discontinuity, string? marker)
    {
        var previous = trail.LastOrDefault();
        if (!discontinuity)
        {
            var dx = point.X - previous.X;
            var dz = point.Z - previous.Z;
            if (dx * dx + dz * dz >= 100) bearing = Math.Atan2(dx, -dz);
        }
        trail.Add((point.X, point.Z, discontinuity, point.Signals?.Speed, marker));
        if (trail.Count <= 4000) return;
        var removable = trail.FindIndex(item => item.Event is null && !item.Break);
        if (removable < 0) return;
        trail.RemoveAt(removable);
    }

    private TelemetrySnapshot? previousSample;

    private bool NeedsPoint(TelemetrySnapshot point)
    {
        if (trail.Count == 0) return true;
        var last = trail[^1];
        var dx = point.X - last.X;
        var dz = point.Z - last.Z;
        var length = dx * dx + dz * dz;
        if (length >= 625) return true;
        if (trail.Count < 2 || length < 64) return false;
        var earlier = trail[^2];
        var oldX = last.X - earlier.X;
        var oldZ = last.Z - earlier.Z;
        var dot = oldX * dx + oldZ * dz;
        return dot <= 0 || dot * dot < .88 * (oldX * oldX + oldZ * oldZ) * length;
    }
    private TelemetrySnapshot? truck;
    private bool interrupted = true;
    private bool english;
    private bool light;
    private string? tripKey;
    private double zoom = 1;
    private Func<double, double, PointF>? lastProjection;

    public long? ReplayTimeNear(Point cursor, IReadOnlyList<RecordedFrame> frames, int lastIndex)
    {
        if (lastProjection is not { } project || !showTrack) return null;
        var nearest = 14f * 14f;
        long? time = null;
        for (var index = 0; index <= lastIndex && index < frames.Count; index++)
        {
            if (frames[index].Trip is not { } trip || !double.IsFinite(trip.X) || !double.IsFinite(trip.Z)) continue;
            var point = project(trip.X, trip.Z);
            var dx = point.X - cursor.X;
            var dy = point.Y - cursor.Y;
            var squared = dx * dx + dy * dy;
            if (squared >= nearest) continue;
            nearest = squared;
            time = frames[index].ElapsedMs;
        }
        return time;
    }
    private PointF pan;
    private Point dragStart;
    private bool dragging;
    private bool manuallyPanned;

    public void Recenter()
    {
        zoom = 1;
        follow = true;
        pan = PointF.Empty;
        fixedCenter = null;
        focusCity = null;
        fitTrail = false;
        manuallyPanned = false;
        Invalidate();
    }

    public void Zoom(double factor)
    {
        zoom = Math.Clamp(zoom * factor, .35, 10);
        Invalidate();
    }

    private IReadOnlyList<RecordedFrame>? replayFrames;
    private int replayThrough = -1;
    private TelemetrySnapshot? previousReplaySample;

    public void ShowReplayTrail(IReadOnlyList<RecordedFrame> frames, int index)
    {
        if (!ReferenceEquals(frames, replayFrames) || index <= replayThrough || trail.Count == 0)
        {
            trail.Clear();
            interrupted = true;
            replayFrames = frames;
            replayThrough = -1;
            previousReplaySample = null;
        }

        for (var at = replayThrough + 1; at <= index; at++)
        {
            var frame = frames[at];
            var marker = frame.Marker is { Length: > 0 } ? "marker" : frame.Event ?? TelemetryReplay.DetectEvents(frame.Trip, previousReplaySample).LastOrDefault();
            previousReplaySample = frame.Trip;
            if (frame.Trip is not { Paused: false } point || !double.IsFinite(point.X) || !double.IsFinite(point.Z)
                || Math.Abs(point.X) > 2_000_000 || Math.Abs(point.Z) > 2_000_000)
            {
                interrupted = true;
            }
            else
            {
                var last = trail.LastOrDefault();
                if (interrupted || marker is not null || NeedsPoint(point) || at == index)
                {
                    var jump = Math.Abs(last.X - point.X) + Math.Abs(last.Z - point.Z) > 4000;
                    AddPoint(point, interrupted || jump, marker);
                    interrupted = false;
                }
            }
        }
        replayThrough = index;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var delta = e.KeyCode switch
        {
            Keys.Up => new Point(0, 24), Keys.Down => new Point(0, -24),
            Keys.Left => new Point(24, 0), Keys.Right => new Point(-24, 0), _ => Point.Empty
        };
        if (e.KeyCode == Keys.Home) Recenter();
        else if (delta != Point.Empty)
        {
            SuspendFollowing();
            manuallyPanned = true;
            pan = new(pan.X + delta.X, pan.Y + delta.Y);
            Invalidate();
        }
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragStart = e.Location;
        dragging = true;
        Capture = true;
        Cursor = Cursors.Hand;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || dragStart == e.Location) return;
        SuspendFollowing();
        manuallyPanned = true;
        pan = new(pan.X + e.X - dragStart.X, pan.Y + e.Y - dragStart.Y);
        dragStart = e.Location;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Zoom(Math.Pow(1.25, e.Delta / 120d));
    }

    public TripMap()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 220;
        Dock = DockStyle.Top;
        AccessibleName = "Mapa del viaje / Trip map";
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public void UpdateReplayTruck(TelemetrySnapshot? position)
    {
        truck = position is { } value && double.IsFinite(value.X) && double.IsFinite(value.Z)
            && Math.Abs(value.X) <= 2_000_000 && Math.Abs(value.Z) <= 2_000_000 ? value : null;
        Invalidate();
    }

    public void UpdateMap(TelemetrySnapshot? snapshot, CityLookup catalog, bool isEnglish, bool isLight, bool demo, bool repaint = true, string? eventKind = null)
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
        origin = truck?.OnJob == true ? catalog.FindDestination(truck.SourceCity, null) : null;
        destination = truck?.OnJob == true ? catalog.FindDestination(truck.DestinationCity, truck.Signals?.DestinationId) : null;
        var marker = eventKind ?? TelemetryReplay.DetectEvents(truck, previousSample).LastOrDefault();
        previousSample = truck;
        if (truck is null) interrupted = true;
        else if (!truck.Paused && !demo)
        {
            var last = trail.LastOrDefault();
            var dx = truck.X - last.X;
            var dz = truck.Z - last.Z;
            var change = dx * dx + dz * dz;
            if (interrupted || marker is not null || NeedsPoint(truck))
            {
                AddPoint(truck, interrupted || change > 16_000_000, marker);
                interrupted = false;
            }
        }
        if (repaint) Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var background = light ? SystemColors.Window : Control.DefaultBackColor;
        var label = Control.DefaultForeColor;
        var accent = light && !SystemInformation.HighContrast ? SystemColors.HotTrack : label;
        g.Clear(background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(24, label));
        for (var x = 0; x < Width; x += 28) g.DrawLine(grid, x, 0, x, Height);
        for (var y = 0; y < Height; y += 28) g.DrawLine(grid, 0, y, Width, y);
        var caption = manuallyPanned ? (english ? "Manual view · Recenter to follow" : "Vista manual · Centra para seguir")
            : follow ? (english ? "Following vehicle" : "Siguiendo el vehículo")
            : english ? "Approximate trip map" : "Mapa aproximado del viaje";
        TextRenderer.DrawText(g, caption, Font, new Rectangle(10, 4, Width - 20, 24), label, TextFormatFlags.EndEllipsis);
        if (truck is null && focusCity is null)
        {
            lastProjection = null;
            TextRenderer.DrawText(g, english ? "The route appears when you enter a drive." : "El recorrido aparece al entrar en una partida.",
                Font, ClientRectangle, label, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
            return;
        }

        var centerX = focusCity?.X ?? fixedCenter?.X ?? truck?.X ?? 0;
        var centerZ = focusCity?.Z ?? fixedCenter?.Z ?? truck?.Z ?? 0;
        var radiusX = 11_000d;
        var radiusZ = 11_000d;
        if (fitTrail && trail.Count > 0)
        {
            var minX = trail.Min(point => point.X);
            var maxX = trail.Max(point => point.X);
            var minZ = trail.Min(point => point.Z);
            var maxZ = trail.Max(point => point.Z);
            centerX = (minX + maxX) / 2;
            centerZ = (minZ + maxZ) / 2;
            radiusX = Math.Max(1000, (maxX - minX) / 2);
            radiusZ = Math.Max(1000, (maxZ - minZ) / 2);
        }
        else if (!follow && focusCity is null && destination is { } goal)
        {
            radiusX = Math.Max(radiusX, Math.Abs(goal.X - centerX));
            radiusZ = Math.Max(radiusZ, Math.Abs(goal.Z - centerZ));
        }
        var angle = headingUp && !fitTrail ? bearing : 0;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var scale = Math.Min(Math.Max(1, Width - 80) / (2 * radiusX), Math.Max(1, Height - 80) / (2 * radiusZ)) * zoom;
        PointF Place(double x, double z)
        {
            var dx = x - centerX;
            var dz = z - centerZ;
            return new((float)(Width / 2d + pan.X + (dx * cosine + dz * sine) * scale),
                (float)(Height / 2d + pan.Y + (-dx * sine + dz * cosine) * scale));
        }
        lastProjection = Place;
        var ruler = Math.Max(24, Math.Min(Width / 4, 80));
        using var scaleLine = new Pen(label, 1.5f);
        using var scaleBacking = new SolidBrush(background);
        g.FillRectangle(scaleBacking, 7, Height - 39, ruler + 24, 35);
        g.DrawLine(scaleLine, 12, Height - 31, 12 + ruler, Height - 31);
        g.DrawLine(scaleLine, 12, Height - 35, 12, Height - 28);
        g.DrawLine(scaleLine, 12 + ruler, Height - 35, 12 + ruler, Height - 28);
        TextRenderer.DrawText(g, $"{ruler / scale:N0} u", Font, new Point(12, Height - 27), label);
        var north = new PointF(Width - 24, 38);
        using var compass = new Pen(label, 2) { EndCap = LineCap.ArrowAnchor };
        var tip = new PointF(north.X - (float)sine * 14, north.Y - (float)cosine * 14);
        g.DrawLine(compass, north, tip);
        TextRenderer.DrawText(g, "N", Font, Point.Round(north), label);

        using var track = new Pen(accent, 2);
        using var olderTrack = new Pen(Color.FromArgb(125, accent), 1.5f);
        using var slow = new Pen(light ? Color.DarkOrange : Color.Gold, 2);
        if (showTrack)
            for (var i = 1; i < trail.Count; i++)
                if (!trail[i].Break)
                    g.DrawLine(colorSpeed && trail[i].Speed is { } speed && Math.Abs(speed) < 12 ? slow
                        : i < trail.Count - 32 ? olderTrack : track,
                        Place(trail[i - 1].X, trail[i - 1].Z), Place(trail[i].X, trail[i].Z));

        if (showTerminals && truck is { } currentTruck && destination is { } goalCity)
        {
            using var guide = new Pen(Color.FromArgb(160, label), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLine(guide, Place(currentTruck.X, currentTruck.Z), Place(goalCity.X, goalCity.Z));
        }

        var occupied = new List<Rectangle> { new(7, Height - 39, ruler + 24, 35), new(0, 0, Width, 24) };
        void Marker(string name, PointF point, Color color, int size, bool priority = false, bool stop = false)
        {
            if (point.X < 8 || point.X > Width - 8 || point.Y < 30 || point.Y > Height - 10) return;
            using var brush = new SolidBrush(color);
            if (stop) g.FillRectangle(brush, point.X - size / 2f, point.Y - size / 2f, size, size);
            else g.FillEllipse(brush, point.X - size / 2f, point.Y - size / 2f, size, size);
            var measured = TextRenderer.MeasureText(name, Font);
            var box = new Rectangle(Math.Clamp((int)point.X + 7, 2, Math.Max(2, Width - measured.Width)),
                Math.Max(26, (int)point.Y - measured.Height), measured.Width, measured.Height);
            if (occupied.Any(other => other.IntersectsWith(box)))
            {
                if (!priority) return;
                box.Y = Math.Clamp((int)point.Y + 7, 26, Math.Max(26, Height - measured.Height - 6));
            }
            using var backing = new SolidBrush(background);
            g.FillRectangle(backing, box);
            TextRenderer.DrawText(g, name, Font, box, color);
            occupied.Add(box);
        }

        if (showTerminals && destination is { } target)
            Marker(target.Name, Place(target.X, target.Z), accent, 12, priority: true, stop: truck?.Coach == true);
        if (showTerminals && origin is { } start) Marker(start.Name, Place(start.X, start.Z), label, 8);
        if (truck is not null)
        {
            var point = Place(truck.X, truck.Z);
            Marker(truck.Coach ? english ? "Coach" : "Autobús" : english ? "Truck" : "Camión", point, accent, 10);
            if (ClientRectangle.Contains(Point.Round(point)))
            {
                using var outline = new Pen(background, 3);
                g.DrawEllipse(outline, point.X - 5, point.Y - 5, 10, 10);
            }
            using var arrow = new Pen(accent, 2) { EndCap = LineCap.ArrowAnchor };
            g.DrawLine(arrow, point, new PointF(point.X + 14 * (float)Math.Sin(bearing - angle),
                point.Y - 14 * (float)Math.Cos(bearing - angle)));
        }
        if (focusCity is { } selected) Marker(selected.Name, Place(selected.X, selected.Z), accent, 8);
        if (highlightedReplayEvent is { } highlight)
            Marker(highlight.Symbol, Place(highlight.Position.X, highlight.Position.Z), accent, 13, priority: true);
        if (showEvents)
            foreach (var point in trail.Where(point => point.Event is not null).TakeLast(24))
            {
                var eventCaption = point.Event switch
                {
                    "pickup" => english ? "Pickup" : "Recogida", "delivery" => english ? "Delivery" : "Entrega",
                    "fuel" => english ? "Refueling" : "Repostaje", "rest" => english ? "Rest" : "Descanso",
                    "ferry" => "Ferry",
                    "route_changed" => english ? "Route recalculated" : "Ruta recalculada",
                    string border when border.StartsWith("border:", StringComparison.Ordinal) =>
                        (english ? "Border" : "Frontera") + $" · {Countries.Name(border[7..], english) ?? border[7..]}",
                    _ => english ? "Marker" : "Marcador"
                };
                var symbol = EventSymbol(point.Event);
                Marker($"{symbol} {eventCaption}", Place(point.X, point.Z), accent, 6);
            }
        if (showCities)
            foreach (var city in cities.Where(city => ClientRectangle.Contains(Point.Round(Place(city.X, city.Z))))
                .OrderBy(city => Math.Abs(centerX - city.X) + Math.Abs(centerZ - city.Z)).Take(12))
                if (city != destination && city != origin && city != focusCity)
                    Marker(city.Name, Place(city.X, city.Z), label, 4);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
    }
}
