using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed class BrowserOverlay : IDisposable
{
    internal const int FirstPort = 17832;
    private const int SchemaVersion = 1;
    private readonly int port;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private volatile string path;
    private volatile string state = "{}";

    public BrowserOverlay(string key)
    {
        path = "/" + key;
        listener = Bind();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cancellation = stop.Token;
        _ = Task.Run(() => ServeAsync(cancellation));
    }

    public string Url => $"http://127.0.0.1:{port}{path}";

    public void Rotate(string key) => path = "/" + key;

    private static TcpListener Bind()
    {
        for (var candidate = FirstPort; candidate < FirstPort + 6; candidate++)
        {
            var server = new TcpListener(IPAddress.Loopback, candidate) { ExclusiveAddressUse = true };
            try
            {
                server.Start(8);
                return server;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                server.Stop();
            }
        }
        throw new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    public void Update(string route, string distance, AppOptions options)
    {
        state = JsonSerializer.Serialize(new
        {
            route = options.ObsRoute ? route : "",
            distance = options.ObsDistance ? distance : "",
            scale = Math.Clamp(options.ObsSize, 75, 150) / 100d,
            font = Math.Clamp(options.ObsFont, 14, 42),
            opacity = Math.Clamp(options.ObsOpacity, 0, 100) / 100d,
            radius = Math.Clamp(options.ObsRadius, 0, 24),
            align = options.ObsPosition switch { "right" => "flex-end", "center" => "center", _ => "flex-start" }
        });
    }

    private async Task ServeAsync(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try { await ReplyAsync(client.GetStream(), timeout.Token); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ReplyAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var input = new byte[4096];
        var length = 0;
        string request = "";
        while (length < input.Length && !request.Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(input.AsMemory(length), cancellation);
            if (read == 0) return;
            length += read;
            request = Encoding.ASCII.GetString(input, 0, length);
        }
        var lines = request.Split("\r\n");
        var hostOk = lines.Any(line => line.Equals($"Host: 127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase));
        var target = lines[0].Split(' ');
        var route = hostOk && target is ["GET", var url, "HTTP/1.1"] ? url : "";
        var currentPath = path;
        var health = route is "/health" || route == currentPath + "/health";
        var json = route == currentPath + "/state";
        var valid = json || health || route == currentPath;
        var payload = Encoding.UTF8.GetBytes(health ? JsonSerializer.Serialize(new { schemaVersion = SchemaVersion })
            : json ? state : valid ? Html : "Not found");
        var contentType = json || health ? "application/json" : "text/html";
        var status = valid ? "200 OK" : "404 Not Found";
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}; charset=utf-8\r\n"
            + $"Content-Length: {payload.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n"
            + "Content-Security-Policy: default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-ancestors 'none'\r\n\r\n");
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(payload, cancellation);
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }

    private const string Html = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>ETS2 · OBS</title><style>
        body{margin:0;background:transparent;display:flex;padding:16px;color:#fff;font:600 22px 'Segoe UI',sans-serif}
        article{background:#1c2530ed;border-radius:8px;padding:12px 18px;max-width:90vw;overflow-wrap:anywhere}
        #distance{font-size:.8em;color:#cae7ff}
        [hidden]{display:none}
        </style></head><body><article hidden><div id="route"></div><div id="distance"></div></article>
        <script>
        const card = document.querySelector('article');
        async function refresh() {
            try {
                const response = await fetch(location.pathname + '/state', {cache:'no-store'});
                if (!response.ok) throw Error();
                const data = await response.json();
                document.querySelector('#route').textContent = data.route || '';
                document.querySelector('#distance').textContent = data.distance || '';
                document.body.style.justifyContent = data.align || 'flex-start';
                card.style.zoom = data.scale ?? 1;
                card.style.fontSize = (data.font ?? 22) + 'px';
                card.style.backgroundColor = 'rgba(28,37,48,' + (data.opacity ?? .93) + ')';
                card.style.borderRadius = (data.radius ?? 8) + 'px';
                card.hidden = !data.route && !data.distance;
            } catch { card.hidden = true; }
            finally { setTimeout(refresh, 500); }
        }
        refresh();
        </script></body></html>
        """;
}
