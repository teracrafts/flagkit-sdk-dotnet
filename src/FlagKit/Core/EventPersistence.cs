using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlagKit.Core;

/// <summary>
/// Event status for persistence tracking.
/// </summary>
public enum PersistedEventStatus
{
    [JsonPropertyName("pending")]
    Pending,

    [JsonPropertyName("sending")]
    Sending,

    [JsonPropertyName("sent")]
    Sent,

    [JsonPropertyName("failed")]
    Failed
}

/// <summary>
/// Persisted event record for crash-resilient event storage.
/// </summary>
public record PersistedEvent
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("status")]
    public PersistedEventStatus Status { get; init; } = PersistedEventStatus.Pending;

    [JsonPropertyName("sentAt")]
    public long? SentAt { get; init; }

    [JsonPropertyName("originalEvent")]
    public AnalyticsEvent? OriginalEvent { get; init; }
}

/// <summary>
/// Crash-resilient event persistence using write-ahead logging.
/// Events are persisted to disk before being queued for sending,
/// ensuring no data loss during unexpected process termination.
/// </summary>
public class EventPersistence : IDisposable
{
    private const string EventFilePrefix = "flagkit-events-";
    private const string EventFileExtension = ".jsonl";
    private const string LockFileName = "flagkit-events.lock";
    private const int DefaultBufferSize = 100;

    private readonly string _storagePath;
    private readonly int _maxEvents;
    private readonly TimeSpan _flushInterval;
    private readonly Action<string>? _logDebug;
    private readonly Action<string>? _logWarning;
    private readonly Action<string>? _logInfo;
    private readonly List<PersistedEvent> _buffer = new();
    private readonly object _bufferLock = new();
    private readonly Timer? _flushTimer;
    private readonly string _currentLogFile;

    private FileStream? _lockFileStream;
    private bool _disposed;

    /// <summary>
    /// Creates a new EventPersistence instance.
    /// </summary>
    /// <param name="storagePath">Directory path for storing event files.</param>
    /// <param name="maxEvents">Maximum number of events to persist (default: 10000).</param>
    /// <param name="flushInterval">Interval between disk writes (default: 1 second).</param>
    /// <param name="logDebug">Optional debug logging callback.</param>
    /// <param name="logWarning">Optional warning logging callback.</param>
    /// <param name="logInfo">Optional info logging callback.</param>
    public EventPersistence(
        string storagePath,
        int maxEvents = 10000,
        TimeSpan? flushInterval = null,
        Action<string>? logDebug = null,
        Action<string>? logWarning = null,
        Action<string>? logInfo = null)
    {
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        _maxEvents = maxEvents > 0 ? maxEvents : 10000;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _logDebug = logDebug;
        _logWarning = logWarning;
        _logInfo = logInfo;

        // Ensure storage directory exists
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }

        // Generate current log file name with timestamp and random suffix
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = Guid.NewGuid().ToString("N")[..8];
        _currentLogFile = Path.Combine(_storagePath, $"{EventFilePrefix}{timestamp}-{random}{EventFileExtension}");

        // Start flush timer
        _flushTimer = new Timer(
            OnFlushTimerCallback,
            null,
            _flushInterval,
            _flushInterval);

        _logDebug?.Invoke($"EventPersistence initialized with storage path: {_storagePath}");
    }

    /// <summary>
    /// Gets the number of events currently in the buffer.
    /// </summary>
    public int BufferCount
    {
        get
        {
            lock (_bufferLock)
            {
                return _buffer.Count;
            }
        }
    }

    /// <summary>
    /// Gets the current log file path.
    /// </summary>
    public string CurrentLogFile => _currentLogFile;

    /// <summary>
    /// Persists an event to the buffer. Automatically flushes if buffer is full.
    /// </summary>
    /// <param name="evt">The analytics event to persist.</param>
    /// <returns>The persisted event ID.</returns>
    public string Persist(AnalyticsEvent evt)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventPersistence));

        var persistedEvent = new PersistedEvent
        {
            Id = $"evt_{Guid.NewGuid():N}",
            Type = evt.EventType.ToString().ToLowerInvariant(),
            Data = evt.Data,
            Timestamp = new DateTimeOffset(evt.Timestamp).ToUnixTimeMilliseconds(),
            Status = PersistedEventStatus.Pending,
            OriginalEvent = evt
        };

        lock (_bufferLock)
        {
            _buffer.Add(persistedEvent);

            // Flush if buffer is full
            if (_buffer.Count >= DefaultBufferSize)
            {
                FlushInternal();
            }
        }

        _logDebug?.Invoke($"Event {persistedEvent.Id} added to persistence buffer");
        return persistedEvent.Id;
    }

    /// <summary>
    /// Flushes buffered events to disk with file locking.
    /// </summary>
    public void Flush()
    {
        if (_disposed) return;

        lock (_bufferLock)
        {
            FlushInternal();
        }
    }

    /// <summary>
    /// Flushes buffered events to disk asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        List<PersistedEvent> eventsToFlush;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;

            eventsToFlush = new List<PersistedEvent>(_buffer);
            _buffer.Clear();
        }

        await WriteEventsAsync(eventsToFlush, cancellationToken);
    }

    /// <summary>
    /// Marks events as sent after successful batch transmission.
    /// </summary>
    /// <param name="eventIds">The IDs of events that were successfully sent.</param>
    public void MarkSent(IEnumerable<string> eventIds)
    {
        if (_disposed) return;

        var idSet = eventIds.ToHashSet();
        if (idSet.Count == 0) return;

        try
        {
            AcquireLock();
            try
            {
                // Append status update entries
                var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                using var stream = new FileStream(
                    _currentLogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);

                using var writer = new StreamWriter(stream);
                foreach (var id in idSet)
                {
                    var statusUpdate = JsonSerializer.Serialize(new
                    {
                        id,
                        status = "sent",
                        sentAt
                    });
                    writer.WriteLine(statusUpdate);
                }

                writer.Flush();
                stream.Flush(flushToDisk: true);

                _logDebug?.Invoke($"Marked {idSet.Count} events as sent");
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to mark events as sent: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks events as sent asynchronously.
    /// </summary>
    /// <param name="eventIds">The IDs of events that were successfully sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MarkSentAsync(IEnumerable<string> eventIds, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        var idSet = eventIds.ToHashSet();
        if (idSet.Count == 0) return;

        try
        {
            AcquireLock();
            try
            {
                var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await using var stream = new FileStream(
                    _currentLogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                await using var writer = new StreamWriter(stream);
                foreach (var id in idSet)
                {
                    var statusUpdate = JsonSerializer.Serialize(new
                    {
                        id,
                        status = "sent",
                        sentAt
                    });
                    await writer.WriteLineAsync(statusUpdate);
                }

                await writer.FlushAsync();
                await stream.FlushAsync(cancellationToken);

                _logDebug?.Invoke($"Marked {idSet.Count} events as sent");
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to mark events as sent: {ex.Message}");
        }
    }

    /// <summary>
    /// Recovers pending events from disk on startup.
    /// </summary>
    /// <returns>List of recovered analytics events.</returns>
    public List<AnalyticsEvent> Recover()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventPersistence));

        var recoveredEvents = new List<AnalyticsEvent>();

        try
        {
            AcquireLock();
            try
            {
                var eventFiles = Directory.GetFiles(_storagePath, $"{EventFilePrefix}*{EventFileExtension}")
                    .OrderBy(f => f)
                    .ToList();

                foreach (var file in eventFiles)
                {
                    var events = ReadEventFile(file);
                    recoveredEvents.AddRange(events);
                }

                _logInfo?.Invoke($"Recovered {recoveredEvents.Count} pending events from {eventFiles.Count} files");
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to recover events from disk: {ex.Message}");
        }

        return recoveredEvents;
    }

    /// <summary>
    /// Recovers pending events from disk asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recovered analytics events.</returns>
    public async Task<List<AnalyticsEvent>> RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventPersistence));

        var recoveredEvents = new List<AnalyticsEvent>();

        try
        {
            AcquireLock();
            try
            {
                var eventFiles = Directory.GetFiles(_storagePath, $"{EventFilePrefix}*{EventFileExtension}")
                    .OrderBy(f => f)
                    .ToList();

                foreach (var file in eventFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var events = await ReadEventFileAsync(file, cancellationToken);
                    recoveredEvents.AddRange(events);
                }

                _logInfo?.Invoke($"Recovered {recoveredEvents.Count} pending events from {eventFiles.Count} files");
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to recover events from disk: {ex.Message}");
        }

        return recoveredEvents;
    }

    /// <summary>
    /// Removes old sent events and compacts event files.
    /// </summary>
    /// <param name="retentionPeriod">How long to keep sent events (default: 24 hours).</param>
    public void Cleanup(TimeSpan? retentionPeriod = null)
    {
        if (_disposed) return;

        var retention = retentionPeriod ?? TimeSpan.FromHours(24);
        var cutoffTime = DateTimeOffset.UtcNow.Subtract(retention).ToUnixTimeMilliseconds();

        try
        {
            AcquireLock();
            try
            {
                var eventFiles = Directory.GetFiles(_storagePath, $"{EventFilePrefix}*{EventFileExtension}")
                    .Where(f => f != _currentLogFile)
                    .ToList();

                foreach (var file in eventFiles)
                {
                    try
                    {
                        // Read all entries from the file
                        var lines = File.ReadAllLines(file);
                        var pendingLines = new List<string>();
                        var eventStatuses = new Dictionary<string, (PersistedEventStatus Status, long? SentAt)>();

                        // First pass: collect status updates
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;

                                if (root.TryGetProperty("id", out var idElement))
                                {
                                    var id = idElement.GetString();
                                    if (id == null) continue;

                                    if (root.TryGetProperty("status", out var statusElement))
                                    {
                                        var statusStr = statusElement.GetString();
                                        long? sentAt = null;
                                        if (root.TryGetProperty("sentAt", out var sentAtElement))
                                        {
                                            sentAt = sentAtElement.GetInt64();
                                        }

                                        var status = statusStr switch
                                        {
                                            "pending" => PersistedEventStatus.Pending,
                                            "sending" => PersistedEventStatus.Sending,
                                            "sent" => PersistedEventStatus.Sent,
                                            "failed" => PersistedEventStatus.Failed,
                                            _ => PersistedEventStatus.Pending
                                        };

                                        eventStatuses[id] = (status, sentAt);
                                    }
                                }
                            }
                            catch
                            {
                                // Skip malformed lines
                            }
                        }

                        // Second pass: filter events
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            try
                            {
                                using var doc = JsonDocument.Parse(line);
                                var root = doc.RootElement;

                                if (!root.TryGetProperty("id", out var idElement)) continue;
                                var id = idElement.GetString();
                                if (id == null) continue;

                                // Check if this is a full event entry (has 'type' field)
                                if (!root.TryGetProperty("type", out _)) continue;

                                // Skip if sent and older than retention period
                                if (eventStatuses.TryGetValue(id, out var statusInfo) &&
                                    statusInfo.Status == PersistedEventStatus.Sent &&
                                    statusInfo.SentAt.HasValue &&
                                    statusInfo.SentAt.Value < cutoffTime)
                                {
                                    continue;
                                }

                                pendingLines.Add(line);
                            }
                            catch
                            {
                                // Skip malformed lines
                            }
                        }

                        // If file is now empty or only has old sent events, delete it
                        if (pendingLines.Count == 0)
                        {
                            File.Delete(file);
                            _logDebug?.Invoke($"Deleted empty event file: {file}");
                        }
                        else if (pendingLines.Count < lines.Length)
                        {
                            // Rewrite compacted file
                            File.WriteAllLines(file, pendingLines);
                            _logDebug?.Invoke($"Compacted event file {file}: {lines.Length} -> {pendingLines.Count} entries");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke($"Failed to cleanup file: {file}: {ex.Message}");
                    }
                }

                _logDebug?.Invoke("Cleanup completed");
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to cleanup event files: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the event persistence, flushing remaining events and cleaning up resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose implementation.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _flushTimer?.Dispose();

            // Final flush of any buffered events
            try
            {
                lock (_bufferLock)
                {
                    FlushInternal();
                }
            }
            catch
            {
                // Ignore errors during disposal
            }

            // Release lock file
            try
            {
                ReleaseLock();
                _lockFileStream?.Dispose();
            }
            catch
            {
                // Ignore errors during disposal
            }
        }

        _disposed = true;
    }

    private void FlushInternal()
    {
        if (_buffer.Count == 0) return;

        var eventsToFlush = new List<PersistedEvent>(_buffer);
        _buffer.Clear();

        WriteEvents(eventsToFlush);
    }

    private void WriteEvents(List<PersistedEvent> events)
    {
        if (events.Count == 0) return;

        try
        {
            AcquireLock();
            try
            {
                using var stream = new FileStream(
                    _currentLogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);

                // Try to lock the file region (not supported on all platforms)
                var lockAcquired = TryLockStream(stream);
                try
                {
                    using var writer = new StreamWriter(stream);
                    foreach (var evt in events)
                    {
                        var json = JsonSerializer.Serialize(evt);
                        writer.WriteLine(json);
                    }

                    writer.Flush();
                    stream.Flush(flushToDisk: true);

                    _logDebug?.Invoke($"Flushed {events.Count} events to disk");
                }
                finally
                {
                    if (lockAcquired)
                    {
                        TryUnlockStream(stream);
                    }
                }
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to flush events to disk, events may be lost: {ex.Message}");

            // Re-buffer events on failure (up to max limit)
            lock (_bufferLock)
            {
                var spaceAvailable = _maxEvents - _buffer.Count;
                var eventsToRestore = events.Take(spaceAvailable).ToList();
                _buffer.InsertRange(0, eventsToRestore);
            }
        }
    }

    private async Task WriteEventsAsync(List<PersistedEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;

        try
        {
            AcquireLock();
            try
            {
                await using var stream = new FileStream(
                    _currentLogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                // Try to lock the file region (not supported on all platforms)
                var lockAcquired = TryLockStream(stream);
                try
                {
                    await using var writer = new StreamWriter(stream);
                    foreach (var evt in events)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var json = JsonSerializer.Serialize(evt);
                        await writer.WriteLineAsync(json);
                    }

                    await writer.FlushAsync();
                    await stream.FlushAsync(cancellationToken);

                    _logDebug?.Invoke($"Flushed {events.Count} events to disk");
                }
                finally
                {
                    if (lockAcquired)
                    {
                        TryUnlockStream(stream);
                    }
                }
            }
            finally
            {
                ReleaseLock();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to flush events to disk, events may be lost: {ex.Message}");

            // Re-buffer events on failure (up to max limit)
            lock (_bufferLock)
            {
                var spaceAvailable = _maxEvents - _buffer.Count;
                var eventsToRestore = events.Take(spaceAvailable).ToList();
                _buffer.InsertRange(0, eventsToRestore);
            }
        }
    }

    private List<AnalyticsEvent> ReadEventFile(string filePath)
    {
        var events = new List<AnalyticsEvent>();
        var eventStatuses = new Dictionary<string, PersistedEventStatus>();

        try
        {
            var lines = File.ReadAllLines(filePath);

            // First pass: collect final status for each event
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("id", out var idElement) &&
                        root.TryGetProperty("status", out var statusElement))
                    {
                        var id = idElement.GetString();
                        var statusStr = statusElement.GetString();

                        if (id != null && statusStr != null)
                        {
                            var status = statusStr switch
                            {
                                "pending" => PersistedEventStatus.Pending,
                                "sending" => PersistedEventStatus.Sending,
                                "sent" => PersistedEventStatus.Sent,
                                "failed" => PersistedEventStatus.Failed,
                                _ => PersistedEventStatus.Pending
                            };
                            eventStatuses[id] = status;
                        }
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Second pass: recover pending/sending events
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var persistedEvent = JsonSerializer.Deserialize<PersistedEvent>(line);
                    if (persistedEvent?.Id == null || persistedEvent.OriginalEvent == null) continue;

                    // Only recover pending or sending (crashed mid-send) events
                    if (eventStatuses.TryGetValue(persistedEvent.Id, out var finalStatus))
                    {
                        if (finalStatus == PersistedEventStatus.Pending ||
                            finalStatus == PersistedEventStatus.Sending)
                        {
                            events.Add(persistedEvent.OriginalEvent);
                        }
                    }
                    else if (persistedEvent.Status == PersistedEventStatus.Pending ||
                             persistedEvent.Status == PersistedEventStatus.Sending)
                    {
                        events.Add(persistedEvent.OriginalEvent);
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to read event file: {filePath}: {ex.Message}");
        }

        return events;
    }

    private async Task<List<AnalyticsEvent>> ReadEventFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var events = new List<AnalyticsEvent>();
        var eventStatuses = new Dictionary<string, PersistedEventStatus>();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);

            // First pass: collect final status for each event
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("id", out var idElement) &&
                        root.TryGetProperty("status", out var statusElement))
                    {
                        var id = idElement.GetString();
                        var statusStr = statusElement.GetString();

                        if (id != null && statusStr != null)
                        {
                            var status = statusStr switch
                            {
                                "pending" => PersistedEventStatus.Pending,
                                "sending" => PersistedEventStatus.Sending,
                                "sent" => PersistedEventStatus.Sent,
                                "failed" => PersistedEventStatus.Failed,
                                _ => PersistedEventStatus.Pending
                            };
                            eventStatuses[id] = status;
                        }
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Second pass: recover pending/sending events
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var persistedEvent = JsonSerializer.Deserialize<PersistedEvent>(line);
                    if (persistedEvent?.Id == null || persistedEvent.OriginalEvent == null) continue;

                    // Only recover pending or sending (crashed mid-send) events
                    if (eventStatuses.TryGetValue(persistedEvent.Id, out var finalStatus))
                    {
                        if (finalStatus == PersistedEventStatus.Pending ||
                            finalStatus == PersistedEventStatus.Sending)
                        {
                            events.Add(persistedEvent.OriginalEvent);
                        }
                    }
                    else if (persistedEvent.Status == PersistedEventStatus.Pending ||
                             persistedEvent.Status == PersistedEventStatus.Sending)
                    {
                        events.Add(persistedEvent.OriginalEvent);
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to read event file: {filePath}: {ex.Message}");
        }

        return events;
    }

    private void AcquireLock()
    {
        if (_lockFileStream != null) return;

        var lockFilePath = Path.Combine(_storagePath, LockFileName);
        _lockFileStream = new FileStream(
            lockFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    private void ReleaseLock()
    {
        _lockFileStream?.Dispose();
        _lockFileStream = null;
    }

    /// <summary>
    /// Tries to lock a file stream. Returns true if successful, false if not supported on the platform.
    /// </summary>
    private static bool TryLockStream(FileStream stream)
    {
        try
        {
            stream.Lock(0, long.MaxValue);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            // FileStream.Lock is not supported on macOS/iOS
            return false;
        }
        catch (IOException)
        {
            // File is already locked by another process
            return false;
        }
    }

    /// <summary>
    /// Tries to unlock a file stream. Silently ignores failures.
    /// </summary>
    private static void TryUnlockStream(FileStream stream)
    {
        try
        {
            stream.Unlock(0, long.MaxValue);
        }
        catch
        {
            // Ignore unlock failures
        }
    }

    private void OnFlushTimerCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            Flush();
        }
        catch
        {
            // Ignore timer callback errors
        }
    }
}
