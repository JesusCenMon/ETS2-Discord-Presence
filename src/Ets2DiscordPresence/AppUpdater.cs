using System.Diagnostics;
using System.Security.Cryptography;

namespace Ets2DiscordPresence;

internal sealed record AppUpdate(Version Version, string Url, string? Sha256);

internal static class AppUpdater
{
    internal const long MaxBytes = 150_000_000;
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
            ValidateSize(response.Content.Headers.ContentLength ?? 0);
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
                    ValidateSize(written);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                }
            }
            VerifyFile(temporary, update.Sha256);
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
        var backupCreated = false;
        var phase = "swap";
        Process? next = null;
        try
        {
            try
            {
                using var parent = Process.GetProcessById(pid);
                if (!parent.WaitForExit(15_000)) throw new IOException("The running app did not close.");
            }
            catch (ArgumentException) { }

            if (action == "update") VerifyFile(payload, expected);
            var legacy = action == "rollback" && ReadVersion(payload) is { } version && version < new Version(0, 7, 2);
            File.Copy(payload, swap, overwrite: true);
            File.Copy(executable, backup, overwrite: false);
            backupCreated = true;
            File.Move(swap, executable, overwrite: true);
            replaced = true;
            phase = "restart";
            var token = Guid.NewGuid().ToString("N");
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEvent(token));
            var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (!legacy)
            {
                launch.ArgumentList.Add("--update-ready");
                launch.ArgumentList.Add(token);
            }
            next = Process.Start(launch) ?? throw new IOException("Could not restart the app.");
            if (legacy ? next.WaitForExit(1800) : !WaitForReady(ready, () => next.HasExited, TimeSpan.FromSeconds(25)))
                throw new IOException("The new instance did not confirm startup.");
            finished = true;
            phase = "swap";
            File.Move(backup, previous, overwrite: true);
            if (action == "update") File.Delete(payload);
            File.Delete(ErrorFile(executable));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            try
            {
                RecordFailure(executable, phase, ex);
                if (replaced && !finished && next is { HasExited: false })
                {
                    next.Kill();
                    if (!next.WaitForExit(5000)) throw new IOException("The new instance is still running.");
                }
                if (replaced && !finished && File.Exists(backup)) File.Move(backup, executable, overwrite: true);
                if (!finished) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception recovery) when (recovery is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        finally
        {
            next?.Dispose();
            TryDelete(swap);
            if (!replaced && backupCreated) TryDelete(backup);
        }
        return true;
    }

    private static string ReadyEvent(string token) => $@"Local\ETS2DiscordPresence.Update.{token}";

    internal static bool WaitForReady(WaitHandle ready, Func<bool> exited, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (ready.WaitOne(50)) return !exited();
            if (exited()) return false;
        } while (clock.Elapsed < timeout);
        return false;
    }

    public static void ConfirmStartup(string[] args)
    {
        var index = Array.IndexOf(args, "--update-ready");
        if (index < 0 || index + 1 >= args.Length || !Guid.TryParseExact(args[index + 1], "N", out _)) return;
        try
        {
            using var ready = EventWaitHandle.OpenExisting(ReadyEvent(args[index + 1]));
            ready.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    public static Version? ReadVersion(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileVersion is null) return null;
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
    }

    public static void RecordFailure(string executable, string phase, Exception exception)
    {
        var message = exception.Message
            .Replace(Path.GetDirectoryName(executable) ?? executable, "[carpeta]", StringComparison.OrdinalIgnoreCase);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (user.Length > 0) message = message.Replace(user, "[usuario]", StringComparison.OrdinalIgnoreCase);
        message = string.Concat(message.Where(character => !char.IsControl(character)).Take(220));
        try { LocalData.WriteFile(ErrorFile(executable), $"{phase} · {exception.GetType().Name} · {message}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string? ConsumeFailure(string executable)
    {
        var error = ErrorFile(executable);
        try
        {
            if (!File.Exists(error)) return null;
            var message = File.ReadAllText(error);
            TryDelete(error);
            return message == "update-failed" ? "" : message[..Math.Min(message.Length, 300)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void CleanWorker(string executable)
    {
        var worker = Worker(executable);
        try
        {
            // A running worker holds its executable open. Its temporary files remain owned by it.
            if (File.Exists(worker))
            {
                using (File.Open(worker, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                File.Delete(worker);
            }
            TryDelete(Pending(executable));
            TryDelete(executable + ".swap.tmp");
            var backup = executable + ".backup.tmp";
            if (!File.Exists(backup)) return;
            var previous = Previous(executable);
            var digest = Digest(backup);
            if (digest == Digest(executable) || File.Exists(previous) && digest == Digest(previous)) TryDelete(backup);
            else if (!File.Exists(previous)) File.Move(backup, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static void ValidateSize(long bytes)
    {
        if (bytes > MaxBytes) throw new InvalidDataException("The release is too large.");
    }

    internal static void VerifyFile(string file, string? expected)
    {
        ValidateSize(new FileInfo(file).Length);
        if (!ValidDigest(expected) || !Digest(file).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded file did not match the release digest.");
    }

    internal static bool ValidDigest(string? digest) => digest?.Length == 64 && digest.All(Uri.IsHexDigit);

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
