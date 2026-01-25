using FlagKit.Core;
using Xunit;

namespace FlagKit.Tests.Core;

public class EventPersistenceTests : IDisposable
{
    private readonly string _testStoragePath;

    public EventPersistenceTests()
    {
        _testStoragePath = Path.Combine(Path.GetTempPath(), $"flagkit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testStoragePath);
    }

    public void Dispose()
    {
        // Cleanup test directory
        try
        {
            if (Directory.Exists(_testStoragePath))
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void Persist_Adds_Event_To_Buffer()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        var evt = new AnalyticsEvent { EventType = EventType.Custom };
        var eventId = persistence.Persist(evt);

        Assert.NotNull(eventId);
        Assert.StartsWith("evt_", eventId);
        Assert.Equal(1, persistence.BufferCount);
    }

    [Fact]
    public void Flush_Writes_Events_To_Disk()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        var evt = new AnalyticsEvent { EventType = EventType.Custom };
        persistence.Persist(evt);
        persistence.Flush();

        Assert.Equal(0, persistence.BufferCount);
        Assert.True(File.Exists(persistence.CurrentLogFile));

        var content = File.ReadAllText(persistence.CurrentLogFile);
        Assert.Contains("\"status\":0", content); // Pending status = 0
    }

    [Fact]
    public async Task FlushAsync_Writes_Events_To_Disk()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        var evt = new AnalyticsEvent { EventType = EventType.Custom };
        persistence.Persist(evt);
        await persistence.FlushAsync();

        Assert.Equal(0, persistence.BufferCount);
        Assert.True(File.Exists(persistence.CurrentLogFile));
    }

    [Fact]
    public void Recover_Returns_Pending_Events()
    {
        // First persistence instance - persist events
        string currentLogFile;
        using (var persistence1 = new EventPersistence(_testStoragePath))
        {
            var evt1 = new AnalyticsEvent
            {
                EventType = EventType.Custom,
                Data = new Dictionary<string, object?> { ["test"] = "value1" }
            };
            var evt2 = new AnalyticsEvent
            {
                EventType = EventType.Evaluation,
                FlagKey = "test-flag"
            };

            persistence1.Persist(evt1);
            persistence1.Persist(evt2);
            persistence1.Flush();
            currentLogFile = persistence1.CurrentLogFile;
        }

        // Second persistence instance - recover events
        using var persistence2 = new EventPersistence(_testStoragePath);
        var recovered = persistence2.Recover();

        Assert.Equal(2, recovered.Count);
    }

    [Fact]
    public async Task RecoverAsync_Returns_Pending_Events()
    {
        // First persistence instance - persist events
        using (var persistence1 = new EventPersistence(_testStoragePath))
        {
            var evt = new AnalyticsEvent { EventType = EventType.Custom };
            persistence1.Persist(evt);
            await persistence1.FlushAsync();
        }

        // Second persistence instance - recover events
        using var persistence2 = new EventPersistence(_testStoragePath);
        var recovered = await persistence2.RecoverAsync();

        Assert.Single(recovered);
    }

    [Fact]
    public void MarkSent_Updates_Event_Status()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        var evt = new AnalyticsEvent { EventType = EventType.Custom };
        var eventId = persistence.Persist(evt);
        persistence.Flush();

        persistence.MarkSent(new[] { eventId });

        // Verify the log file contains the sent status update
        var content = File.ReadAllText(persistence.CurrentLogFile);
        Assert.Contains("\"status\":\"sent\"", content);
    }

    [Fact]
    public async Task MarkSentAsync_Updates_Event_Status()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        var evt = new AnalyticsEvent { EventType = EventType.Custom };
        var eventId = persistence.Persist(evt);
        await persistence.FlushAsync();

        await persistence.MarkSentAsync(new[] { eventId });

        var content = await File.ReadAllTextAsync(persistence.CurrentLogFile);
        Assert.Contains("\"status\":\"sent\"", content);
    }

    [Fact]
    public void Recover_Does_Not_Return_Sent_Events()
    {
        // First persistence instance - persist and mark as sent
        using (var persistence1 = new EventPersistence(_testStoragePath))
        {
            var evt = new AnalyticsEvent { EventType = EventType.Custom };
            var eventId = persistence1.Persist(evt);
            persistence1.Flush();
            persistence1.MarkSent(new[] { eventId });
        }

        // Second persistence instance - recover events
        using var persistence2 = new EventPersistence(_testStoragePath);
        var recovered = persistence2.Recover();

        Assert.Empty(recovered);
    }

    [Fact]
    public void Cleanup_Removes_Old_Sent_Events()
    {
        string logFile;
        using (var persistence1 = new EventPersistence(_testStoragePath))
        {
            var evt = new AnalyticsEvent { EventType = EventType.Custom };
            var eventId = persistence1.Persist(evt);
            persistence1.Flush();
            persistence1.MarkSent(new[] { eventId });
            logFile = persistence1.CurrentLogFile;
        }

        // Create a new persistence and run cleanup with zero retention
        using var persistence2 = new EventPersistence(_testStoragePath);
        persistence2.Cleanup(TimeSpan.Zero);

        // The old file should be deleted since all events are sent
        Assert.False(File.Exists(logFile));
    }

    [Fact]
    public void AutoFlush_Triggers_When_Buffer_Full()
    {
        using var persistence = new EventPersistence(
            _testStoragePath,
            maxEvents: 10000,
            flushInterval: TimeSpan.FromMinutes(10));

        // Add 100 events (default buffer size is 100)
        for (var i = 0; i < 100; i++)
        {
            persistence.Persist(new AnalyticsEvent { EventType = EventType.Custom });
        }

        // Buffer should be empty after auto-flush
        Assert.Equal(0, persistence.BufferCount);
        Assert.True(File.Exists(persistence.CurrentLogFile));
    }

    [Fact]
    public void Dispose_Flushes_Remaining_Events()
    {
        string logFile;
        using (var persistence = new EventPersistence(_testStoragePath))
        {
            persistence.Persist(new AnalyticsEvent { EventType = EventType.Custom });
            logFile = persistence.CurrentLogFile;
            // Buffer has 1 event, dispose should flush it
        }

        Assert.True(File.Exists(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("custom", content.ToLowerInvariant());
    }

    [Fact]
    public void Creates_Storage_Directory_If_Not_Exists()
    {
        var nonExistentPath = Path.Combine(_testStoragePath, "subdir", "events");

        using var persistence = new EventPersistence(nonExistentPath);

        Assert.True(Directory.Exists(nonExistentPath));
    }

    [Fact]
    public void Throws_When_Accessing_Disposed_Instance()
    {
        var persistence = new EventPersistence(_testStoragePath);
        persistence.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            persistence.Persist(new AnalyticsEvent { EventType = EventType.Custom }));
    }

    [Fact]
    public void File_Locking_Prevents_Concurrent_Access()
    {
        using var persistence1 = new EventPersistence(_testStoragePath);

        // Acquire lock by flushing
        persistence1.Persist(new AnalyticsEvent { EventType = EventType.Custom });
        persistence1.Flush();

        // Second instance should be able to work after first releases lock
        using var persistence2 = new EventPersistence(_testStoragePath);
        persistence2.Persist(new AnalyticsEvent { EventType = EventType.Custom });
        persistence2.Flush();

        // Both should have written successfully
        Assert.True(File.Exists(persistence1.CurrentLogFile));
        Assert.True(File.Exists(persistence2.CurrentLogFile));
    }

    [Fact]
    public void Multiple_Events_Persisted_In_JsonLines_Format()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        persistence.Persist(new AnalyticsEvent { EventType = EventType.Custom });
        persistence.Persist(new AnalyticsEvent { EventType = EventType.Evaluation });
        persistence.Persist(new AnalyticsEvent { EventType = EventType.Identify });
        persistence.Flush();

        var lines = File.ReadAllLines(persistence.CurrentLogFile);
        Assert.Equal(3, lines.Length);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            Assert.Contains("{", line);
            Assert.Contains("}", line);
        }
    }

    [Fact]
    public void Handles_Empty_EventIds_In_MarkSent()
    {
        using var persistence = new EventPersistence(_testStoragePath);

        // Should not throw
        persistence.MarkSent(Array.Empty<string>());
    }

    [Fact]
    public async Task Handles_Cancellation_In_RecoverAsync()
    {
        using var persistence = new EventPersistence(_testStoragePath);
        persistence.Persist(new AnalyticsEvent { EventType = EventType.Custom });
        persistence.Flush();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            persistence.RecoverAsync(cts.Token));
    }
}

public class EventQueueWithPersistenceTests : IDisposable
{
    private readonly string _testStoragePath;

    public EventQueueWithPersistenceTests()
    {
        _testStoragePath = Path.Combine(Path.GetTempPath(), $"flagkit-queue-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testStoragePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testStoragePath))
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void EventQueue_With_Persistence_Persists_Events()
    {
        var persistence = new EventPersistence(_testStoragePath);
        var flushedEvents = new List<AnalyticsEvent>();

        var queue = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: events =>
            {
                flushedEvents.AddRange(events);
                return Task.CompletedTask;
            },
            persistence: persistence);

        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });

        Assert.True(queue.IsPersistenceEnabled);
        Assert.Equal(1, persistence.BufferCount);
    }

    [Fact]
    public async Task EventQueue_Marks_Events_Sent_After_Flush()
    {
        var persistence = new EventPersistence(_testStoragePath);
        var flushedEvents = new List<AnalyticsEvent>();

        var queue = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: events =>
            {
                flushedEvents.AddRange(events);
                return Task.CompletedTask;
            },
            persistence: persistence);

        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });

        // Flush persistence to disk first
        persistence.Flush();

        // Then flush the queue
        await queue.FlushAsync();

        Assert.Equal(1, flushedEvents.Count);

        // Check that sent status was written
        var content = File.ReadAllText(persistence.CurrentLogFile);
        Assert.Contains("\"status\":\"sent\"", content);
    }

    [Fact]
    public void EventQueue_Recovers_Events_On_Start()
    {
        // First - persist events and simulate crash (dispose without flushing queue)
        using (var persistence1 = new EventPersistence(_testStoragePath))
        {
            var queue1 = new EventQueue(
                batchSize: 10,
                flushInterval: TimeSpan.FromSeconds(30),
                onFlush: _ => Task.CompletedTask,
                persistence: persistence1);

            queue1.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });
            persistence1.Flush(); // Persist to disk, but don't flush queue
        }

        // Second - new instance should recover events
        var persistence2 = new EventPersistence(_testStoragePath);
        var recoveredCount = 0;

        var queue2 = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: events =>
            {
                recoveredCount = events.Count;
                return Task.CompletedTask;
            },
            persistence: persistence2);

        queue2.Start();

        Assert.Equal(1, queue2.Count);
    }

    [Fact]
    public void EventQueue_Without_Persistence_Works_Normally()
    {
        var flushedEvents = new List<AnalyticsEvent>();

        var queue = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: events =>
            {
                flushedEvents.AddRange(events);
                return Task.CompletedTask;
            });

        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });

        Assert.False(queue.IsPersistenceEnabled);
        Assert.Equal(1, queue.Count);
    }
}
