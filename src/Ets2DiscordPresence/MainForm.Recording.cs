namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private bool recordingAuto;
    private bool recordingPaused;
    private long recordingPausedAt;
    private int recordingFrames;
    private string? manualStopJob;
    private string? recordedJob;
    private long nextBackupCheck;
    private long nextQuotaCheck;
    private long storedBytes;
    private bool markerHotkeyRegistered;

    private static string? JobId(TelemetrySnapshot? snapshot) => snapshot switch
    {
        { OnJob: true, Coach: true, DestinationCity.Length: > 0 } =>
            $"coach|{snapshot.SourceCity}|{snapshot.DestinationCity}|{snapshot.Signals?.DestinationId}",
        { OnJob: true, Coach: false, Signals: { } signals } =>
            $"{signals.Deadline}|{signals.JobKey}|{snapshot.SourceCity}|{snapshot.DestinationCity}",
        _ => null
    };

    private bool StartRecording(bool automatic, long now, string? job)
    {
        try
        {
            if (!HasRecordingSpace())
            {
                settingsError = L("Se alcanzó el límite de espacio de grabaciones.", "The recording space limit was reached.");
                return false;
            }
            Directory.CreateDirectory(LocalData.Recordings);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var file = Path.Combine(LocalData.Recordings, $"ETS2-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{suffix}.jsonl");
            recording = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            recordingPath = file;
            recordingFrames = 0;
            TelemetryReplay.WriteHeader(recording);
            recording.Flush();
            recordingStarted = now;
            recordingFrames = 0;
            RegisterPrivacyShortcut();
            recordingAuto = automatic;
            recordedJob = job;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (recording is not null) FinishRecording();
            settingsError = L("No se pudo abrir el archivo de grabación.", "Could not open the recording file.");
            return false;
        }
    }

    private bool HasRecordingSpace()
    {
        var limit = preferences.Features.RecordingLimitMb;
        if (limit <= 0) return true;
        var now = Environment.TickCount64;
        if (now >= nextQuotaCheck)
        {
            storedBytes = LocalData.RecordingFiles().Where(path => path != recordingPath)
                .Concat(Directory.Exists(LocalData.RecordingTrash)
                    ? Directory.EnumerateFiles(LocalData.RecordingTrash, "ETS2-*.jsonl*") : [])
                .Sum(path => new FileInfo(path).Length);
            nextQuotaCheck = now + 10_000;
        }
        var bytes = storedBytes + (recording?.BaseStream.Length ?? 0);
        var capacity = (long)limit * 1024 * 1024;
        if (bytes >= capacity * .9 && bytes < capacity)
            quotaWarning = L("Queda menos del 10 % del espacio para grabaciones.", "Less than 10% of recording space remains.");
        else quotaWarning = null;
        return bytes < capacity;
    }

    private enum RecordingFinish { Saved, Empty, Recoverable, Failed }

    private RecordingFinish FinishRecording()
    {
        var path = recordingPath;
        var empty = recordingFrames == 0;
        try
        {
            recording?.Dispose();
            if (empty && path is not null) File.Delete(path);
            else if (path is not null)
            {
                var saved = TelemetryReplay.ReadDetailed(path);
                if (saved.RecoveredTail) return RecordingFinish.Recoverable;
                TelemetryReplay.Save(path, saved.Frames, saved.Metadata?.Created);
            }
            return empty || path is null ? RecordingFinish.Empty : RecordingFinish.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            settingsError = L("No se pudo cerrar la grabación correctamente.", "The recording could not be closed cleanly.");
            return path is not null && File.Exists(path) ? RecordingFinish.Recoverable : RecordingFinish.Failed;
        }
        finally
        {
            recording = null;
            recordingPath = null;
            recordingAuto = recordingPaused = false;
            recordedJob = null;
            nextQuotaCheck = 0;
            if (!Disposing && !IsDisposed) RegisterPrivacyShortcut();
        }
    }

    private void UpdateRecording(TelemetrySnapshot? snapshot, TripEntry? ending, long now)
    {
        var job = JobId(snapshot);
        if (job is not null && job != manualStopJob) manualStopJob = null;
        if (recording is not null && recordingAuto && ending is null
            && (!gameRunning || !preferences.Features.AutoRecord || job is not null && job != recordedJob))
            FinishRecording();
        if (recording is null && ending is null && preferences.Features.AutoRecord && !demo && demoScenario is null && !replayOpen
            && job is not null && job != manualStopJob)
        {
            if (!StartRecording(automatic: true, now, job)) manualStopJob = job;
        }
        if (recording is null) return;
        var nextCoachRoute = ending?.JourneyType == "coach" && job != recordedJob;
        if (!recordingPaused && !nextCoachRoute && (snapshot is not null || recordingFrames > 0))
        {
            AppendFrame(snapshot, now, eventKind: routeRecalculated ? "route_changed" : null);
            if (journal.BoundaryCountry is { } border && recording is not null)
                AppendFrame(snapshot, now, eventKind: $"border:{border}");
        }
        if (ending is not null && recordedJob is { } activeJob
            && activeJob.StartsWith("coach|", StringComparison.Ordinal) == (ending.JourneyType == "coach")
            && recordingPath is { } path && recordingFrames > 0)
        {
            journal.AttachRecording(ending.Id, Path.GetFileName(path));
            if (recordingAuto) FinishRecording();
            else recordedJob = null;
        }
        else if (recordingAuto && snapshot is { OnJob: false })
        {
            FinishRecording();
        }
    }

    private void AppendFrame(TelemetrySnapshot? snapshot, long now, string? marker = null, string? eventKind = null)
    {
        if (recording is null) return;
        try
        {
            if (recordingFrames >= TelemetryReplay.MaximumFrames || recording.BaseStream.Length >= TelemetryReplay.MaximumBytes - 16384
                || !HasRecordingSpace())
            {
                manualStopJob = recordedJob;
                FinishRecording();
                settingsError = L("La grabación se cerró al alcanzar su límite.", "Recording stopped at its limit.");
                return;
            }
            TelemetryReplay.Write(recording, new RecordedFrame(now - recordingStarted, snapshot, marker, eventKind));
            recordingFrames++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            manualStopJob = recordedJob;
            FinishRecording();
            settingsError = L("No se pudo guardar la grabación.", "The recording could not be saved.");
        }
    }

    private void PauseRecording()
    {
        if (recording is null) return;
        var now = Environment.TickCount64;
        recordingPaused = !recordingPaused;
        if (recordingPaused) recordingPausedAt = now;
        else recordingStarted += now - recordingPausedAt;
    }

    private void AddRecordingMarker()
    {
        if (recording is null || recordingPaused || liveSnapshot is null) return;
        AppendFrame(liveSnapshot, Environment.TickCount64, L("Marcador manual", "Manual marker"));
    }

    private void RunScheduledBackup()
    {
        var folder = preferences.Features.BackupFolder;
        if (string.IsNullOrWhiteSpace(folder) || Environment.TickCount64 < nextBackupCheck) return;
        nextBackupCheck = Environment.TickCount64 + 60_000;
        try
        {
            if (!Path.IsPathFullyQualified(folder)) return;
            var interval = Math.Clamp(preferences.Features.BackupIntervalDays, 1, 30);
            var existing = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "ETS2-automatico*.ets2backup")
                .Where(path => Path.GetFileName(path) == "ETS2-automatico.ets2backup" ||
                    System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^ETS2-automatico-\d{8}-\d{6}\.ets2backup$"))
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : [];
            if (existing.Length > 0 && File.GetLastWriteTimeUtc(existing[0]) > DateTime.UtcNow.AddDays(-interval)) return;
            journal.Flush();
            if (journal.Error is not null) return;
            var path = Path.Combine(folder, $"ETS2-automatico-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ets2backup");
            SaveBackup(path);
            foreach (var old in existing.Skip(Math.Clamp(preferences.Features.BackupKeep, 5, 20) - 1))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            settingsError = L("La copia automática no pudo guardarse.", "The automatic backup could not be saved.");
        }
    }
}
