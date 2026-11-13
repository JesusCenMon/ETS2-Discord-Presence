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

    public static async Task<GameScreen?> ReadAsync()
    {
        try
        {
            var bytes = await Task.Run(CaptureGame);
            return bytes is null ? null : await RecognizeAsync(bytes);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException
            or System.ComponentModel.Win32Exception or IOException or NotSupportedException)
        {
            return null;
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
        return Classify((await engine.RecognizeAsync(bitmap)).Text);
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
        if ((photoControl && (lens || Has("photo mode", "modo foto", "modo fotografia")))
            || (lens && Has("focus distance", "distancia de enfoque", "blur strength", "intensidad de desenfoque"))) return GameScreen.Photo;

        var mapControls = Has("set waypoint", "add waypoint", "reset navigation", "map legend", "map filters",
            "anadir punto de ruta", "restablecer navegacion", "reiniciar navegacion", "leyenda del mapa", "filtros del mapa",
            "distancia de la ruta", "route distance", "clear route", "borrar ruta");
        if (Has("world map", "mapa del mundo", "mapa mundial") && mapControls) return GameScreen.Map;
        return null;
    }

    private static byte[]? CaptureGame()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0) return null;
        using var process = Process.GetProcessById((int)processId);
        if (!process.ProcessName.Equals("eurotrucks2", StringComparison.OrdinalIgnoreCase)
            || window != process.MainWindowHandle || !GetClientRect(window, out var client)) return null;
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin)) return null;
        var area = new Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
        if (area.Width is < 320 or > 8192 || area.Height is < 240 or > 4320 || !SystemInformation.VirtualScreen.Contains(area)) return null;
        if (!GameOwnsCaptureArea(window, area)) return null;
        using var frame = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var drawing = Graphics.FromImage(frame))
            drawing.CopyFromScreen(area.Location, Point.Empty, area.Size);
        if (!GameOwnsCaptureArea(window, area)) return null;
        var scale = Math.Min(1d, 1600d / Math.Max(area.Width, area.Height));
        using var scaled = new Bitmap(frame, new Size((int)(area.Width * scale), (int)(area.Height * scale)));
        using var buffer = new MemoryStream();
        scaled.Save(buffer, ImageFormat.Png);
        return buffer.ToArray();
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

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
