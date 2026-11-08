using System.Diagnostics;
using System.Security.Cryptography;

namespace Ets2DiscordPresence;

internal sealed record AppUpdate(Version Version, string Url, string? Sha256);

internal static class AppUpdater
{
    private const long MaxBytes = 150_000_000;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string Previous(string executable) => executable + ".previous.exe";
    private static string Worker(string executable) => executable + ".updater.exe";
    private static string Pending(string executable) => executable + ".pending.exe";
    private static string ErrorFile(string executable) => executable + ".update-error.txt";

    public static async Task DownloadAsync(AppUpdate update, string executable, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        cancellation = timeout.Token;
        if (!ValidDigest(update.Sha256)) throw new InvalidDataException("The release has no verifiable SHA-256 digest.");
        var pending = Pending(executable);
        var temporary = $"{pending}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await Client.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxBytes) throw new InvalidDataException("The release is too large.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellation);
                    if (read == 0) break;
                    written += read;
                    if (written > MaxBytes) throw new InvalidDataException("The release is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                }
            }
            if (!string.Equals(Digest(temporary), update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded file did not match the release digest.");
            File.Move(temporary, pending, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public static void Start(string executable, bool rollback, string? expected)
    {
        var worker = Worker(executable);
        File.Copy(executable, worker, overwrite: true);
        var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--update-worker");
        start.ArgumentList.Add(executable);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(rollback ? "rollback" : "update");
        start.ArgumentList.Add(expected ?? "");
        using var process = Process.Start(start) ?? throw new IOException("Could not start the updater.");
    }

    public static bool HandleWorker(string[] args)
    {
        if (args.Length == 0 || args[0] != "--update-worker") return false;
        if (args is not [_, var executable, var rawPid, var action, var expected]
            || !Path.IsPathFullyQualified(executable) || !int.TryParse(rawPid, out var pid)
            || !string.Equals(Environment.ProcessPath, Worker(executable), StringComparison.OrdinalIgnoreCase)
            || action is not ("update" or "rollback")) return true;

        var payload = action == "rollback" ? Previous(executable) : Pending(executable);
        var swap = executable + ".swap.tmp";
        var backup = executable + ".backup.tmp";
        var previous = Previous(executable);
        var replaced = false;
        var finished = false;
        try
        {
            try
            {
                using var parent = Process.GetProcessById(pid);
                if (!parent.WaitForExit(15_000)) throw new IOException("The running app did not close.");
            }
            catch (ArgumentException) { }

            if (action == "update" && (!ValidDigest(expected) || !Digest(payload).Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The update digest did not match.");
            File.Copy(payload, swap, overwrite: true);
            File.Copy(executable, backup, overwrite: true);
            File.Move(swap, executable, overwrite: true);
            replaced = true;
            using var next = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })
                ?? throw new IOException("Could not restart the app.");
            if (next.WaitForExit(1800)) throw new IOException("The updated app closed immediately.");
            finished = true;
            File.Move(backup, previous, overwrite: true);
            if (action == "update") File.Delete(payload);
            File.Delete(ErrorFile(executable));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            try
            {
                LocalData.WriteFile(ErrorFile(executable), "update-failed");
                if (replaced && !finished && File.Exists(backup)) File.Move(backup, executable, overwrite: true);
                if (!finished) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception recovery) when (recovery is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        finally
        {
            TryDelete(swap);
            if (!replaced) TryDelete(backup);
        }
        return true;
    }

    public static bool ConsumeFailure(string executable)
    {
        var error = ErrorFile(executable);
        if (!File.Exists(error)) return false;
        TryDelete(error);
        return true;
    }

    public static void CleanWorker(string executable) => TryDelete(Worker(executable));

    private static bool ValidDigest(string? digest) => digest?.Length == 64 && digest.All(Uri.IsHexDigit);

    private static string Digest(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
