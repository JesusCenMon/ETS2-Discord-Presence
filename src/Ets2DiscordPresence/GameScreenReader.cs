using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Ets2DiscordPresence;

internal static class GameScreenReader
{
    public static bool HasRecognitionLanguage => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    internal enum ScanStatus { GameNotInForeground, CaptureUnavailable, NoMatch, Recognized }
    internal readonly record struct Scan(GameScreen? Screen, ScanStatus Status);

    public static async Task<Scan> ReadAsync()
    {
        try
        {
            var capture = await Task.Run(CaptureGame);
            if (capture.Bytes is null) return new(null, capture.Status);
            var screen = await RecognizeAsync(capture.Bytes);
            return GetForegroundWindow() != capture.Window ? new(null, ScanStatus.GameNotInForeground)
                : new(screen, screen.HasValue ? ScanStatus.Recognized : ScanStatus.NoMatch);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException
            or System.ComponentModel.Win32Exception or IOException or NotSupportedException)
        {
            return new(null, ScanStatus.CaptureUnavailable);
        }
    }

    internal static async Task<GameScreen?> RecognizeAsync(byte[] bytes)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return null;
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }
        var decoder = await BitmapDecoder.CreateAsync(input);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var full = await engine.RecognizeAsync(bitmap);
        if (Classify(full.Text) is { } recognized) return recognized;

        using var source = new MemoryStream(bytes);
        using var image = new Bitmap(source);
        var mapHeader = new Rectangle(0, 0, image.Width / 2, Math.Max(1, image.Height / 7));
        var photoControls = new Rectangle(image.Width * 4 / 5, image.Height / 2,
            image.Width - image.Width * 4 / 5, image.Height - image.Height / 2);
        if (HasPhotoReticle(image)) return GameScreen.Photo;
        var heading = await ReadRegionAsync(engine, image, mapHeader);
        if (Classify(heading) == GameScreen.Map) return GameScreen.Map;
        var controls = await ReadRegionAsync(engine, image, photoControls);
        return Classify(controls);
    }

    internal static bool HasPhotoReticle(Bitmap image)
    {
        // Controller photo mode draws four white corners around the focus circle.
        // Check both arms of each corner and reject long continuous edges.
        if (image.Width < 640 || image.Height < 360) return false;
        var scale = Math.Min(image.Width / 1920d, image.Height / 1080d);
        var tolerance = Math.Max(1, (int)Math.Ceiling(2 * scale));
        bool Bright(int x, int y)
        {
            if ((uint)x >= image.Width || (uint)y >= image.Height) return false;
            var pixel = image.GetPixel(x, y);
            return Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) >= 158
                && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) < 34;
        }

        bool Arm(int x, int y, int dx, int dy)
        {
            int count = 0, background = 0;
            for (var step = 7; step <= 37; step += 3)
            {
                var along = (int)Math.Round(step * scale);
                for (var offset = -tolerance; offset <= tolerance; offset++)
                    if (Bright(x + dx * along + dy * offset, y + dy * along + dx * offset))
                    {
                        count++;
                        break;
                    }
                var outside = tolerance + 4;
                if (Bright(x + dx * along + dy * outside, y + dy * along + dx * outside)
                    && Bright(x + dx * along - dy * outside, y + dy * along - dx * outside)) background++;
            }
            return count >= 9 && background <= 2;
        }

        var centerX = image.Width * .514;
        var centerY = image.Height * .490;
        var matched = 0;
        foreach (var (side, vertical) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var x = (int)Math.Round(centerX + side * 149 * scale);
            var y = (int)Math.Round(centerY + vertical * 102 * scale);
            var found = false;
            for (var offsetY = -tolerance; offsetY <= tolerance && !found; offsetY++)
            for (var offsetX = -tolerance; offsetX <= tolerance && !found; offsetX++)
                found = Arm(x + offsetX, y + offsetY, -side, 0) && Arm(x + offsetX, y + offsetY, 0, -vertical);
            if (found) matched++;
        }
        return matched == 4;
    }

    private static async Task<string> ReadRegionAsync(OcrEngine engine, Bitmap image, Rectangle region)
    {
        if (region.Width < 1 || region.Height < 1) return "";
        var scale = Math.Min(2d, OcrEngine.MaxImageDimension / (double)Math.Max(region.Width, region.Height));
        using var crop = new Bitmap((int)(region.Width * scale), (int)(region.Height * scale));
        using (var graphics = Graphics.FromImage(crop))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, new Rectangle(Point.Empty, crop.Size), region, GraphicsUnit.Pixel);
        }
        using var bytes = new MemoryStream();
        crop.Save(bytes, ImageFormat.Png);
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes.ToArray());
            await writer.StoreAsync();
        }
        var decoder = await BitmapDecoder.CreateAsync(input);
        using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return (await engine.RecognizeAsync(pixels)).Text;
    }

    internal static GameScreen? Classify(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        var words = " " + string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
        bool Has(params string[] labels) => labels.Any(label => words.Contains(" " + label + " ", StringComparison.Ordinal));
        var photoControl = Has("take photo", "take picture", "tomar foto", "hacer foto", "tomar fotografia",
            "hacer una foto", "capturar foto", "hide ui", "ocultar interfaz");
        var lens = Has("field of view", "campo de vision", "depth of field", "profundidad de campo", "focal length", "distancia focal");
        var photoTitle = Has("photo mode", "modo foto", "modo fotografia", "modo de fotografia", "modo de foto");
        var cameraControls = Has("focus distance", "distancia de enfoque", "blur strength", "intensidad de desenfoque",
            "roll camera", "rotar camara", "camera roll", "camera tilt", "inclinacion de camara", "restablecer camara", "reset camera", "abrir album fotografico", "open photo album", "color saturation", "saturacion");
        var photoPanel = Has("ajustes del modo foto", "photo mode settings", "abrir album fotografico", "open photo album");
        if ((photoPanel && (photoControl || cameraControls && Has("ocultar controles", "hide controls")))
            || (photoControl && (lens || photoTitle)) || (lens && cameraControls)
            || (photoTitle && (lens || cameraControls))) return GameScreen.Photo;

        var mapControls = Has("set waypoint", "add waypoint", "reset navigation", "map legend", "map filters",
            "anadir punto de ruta", "restablecer navegacion", "reiniciar navegacion", "leyenda del mapa", "filtros del mapa",
            "distancia de la ruta", "route distance", "clear route", "borrar ruta");
        var mapTitle = Has("world map", "mapa del mundo", "mapa mundial", "mapa global");
        var mapStats = Has("discovered", "explored", "descubierto", "explorado", "ciudades descubiertas", "discovered cities", "exploracion", "exploration");
        var mapRoute = Has("travel time", "tiempo de viaje", "tiempo del viaje", "route length",
            "longitud de la ruta", "route advisor", "distance to destination", "distancia al destino");
        if ((mapTitle && (mapControls || mapStats || mapRoute))
            || (mapControls && mapStats && mapRoute)) return GameScreen.Map;
        return null;
    }

    private static (byte[]? Bytes, ScanStatus Status, nint Window) CaptureGame()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
            return (null, ScanStatus.GameNotInForeground, 0);
        using var process = Process.GetProcessById((int)processId);
        if (!process.ProcessName.Equals("eurotrucks2", StringComparison.OrdinalIgnoreCase))
            return (null, ScanStatus.GameNotInForeground, 0);
        if (!GetClientRect(window, out var client)) return (null, ScanStatus.CaptureUnavailable, window);
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin)) return (null, ScanStatus.CaptureUnavailable, window);
        var area = new Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
        if (area.Width is < 320 or > 8192 || area.Height is < 240 or > 4320)
            return (null, ScanStatus.CaptureUnavailable, window);

        using var frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        using (var drawing = Graphics.FromImage(frame))
        {
            if (SystemInformation.VirtualScreen.Contains(area) && GameOwnsCaptureArea(window, area))
            {
                drawing.CopyFromScreen(area.Location, Point.Empty, area.Size);
                if (!GameOwnsCaptureArea(window, area)) return (null, ScanStatus.CaptureUnavailable, window);
            }
            else
            {
                // Capture only the game's own client when it overlaps another window.
                var dc = drawing.GetHdc();
                bool rendered;
                try { rendered = PrintWindow(window, dc, 3); }
                finally { drawing.ReleaseHdc(dc); }
                if (!rendered) return (null, ScanStatus.CaptureUnavailable, window);
            }
        }
        if (GetForegroundWindow() != window) return (null, ScanStatus.GameNotInForeground, 0);
        var scale = Math.Min(1d, Math.Min(2560d, OcrEngine.MaxImageDimension) / Math.Max(area.Width, area.Height));
        using var scaled = new Bitmap(frame, new Size((int)(area.Width * scale), (int)(area.Height * scale)));
        if (!HasVisibleContent(scaled)) return (null, ScanStatus.CaptureUnavailable, window);
        using var buffer = new MemoryStream();
        scaled.Save(buffer, ImageFormat.Png);
        return (buffer.ToArray(), ScanStatus.NoMatch, window);
    }

    internal static bool HasVisibleContent(Bitmap frame)
    {
        var first = frame.GetPixel(0, 0);
        for (var y = 1; y < 20; y++)
        for (var x = 1; x < 20; x++)
            if (frame.GetPixel(x * frame.Width / 20, y * frame.Height / 20) != first) return true;
        return false;
    }

    private static bool GameOwnsCaptureArea(nint window, Rectangle area)
    {
        if (GetForegroundWindow() != window) return false;
        foreach (var x in new[] { area.Left + 4, area.Left + area.Width / 2, area.Right - 5 })
        foreach (var y in new[] { area.Top + 4, area.Top + area.Height / 2, area.Bottom - 5 })
            if (GetAncestor(WindowFromPoint(new NativePoint { X = x, Y = y }), 2) != window) return false;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
