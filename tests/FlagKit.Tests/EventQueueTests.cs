using Xunit;

namespace FlagKit.Tests;

public class EventQueueTests
{
    [Fact]
    public void Enqueue_Adds_Event_To_Queue()
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

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task Flush_Sends_All_Events()
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
        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Evaluation });

        await queue.FlushAsync();

        Assert.Equal(2, flushedEvents.Count);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Flushes_When_Batch_Size_Reached()
    {
        var flushedEvents = new List<AnalyticsEvent>();
        var queue = new EventQueue(
            batchSize: 2,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: events =>
            {
                flushedEvents.AddRange(events);
                return Task.CompletedTask;
            });

        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });
        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });

        // Give async flush time to complete
        await Task.Delay(50);

        Assert.Equal(2, flushedEvents.Count);
    }

    [Fact]
    public void TrackEvaluation_Creates_Evaluation_Event()
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

        queue.TrackEvaluation("test-flag", true);

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TrackCustom_Creates_Custom_Event()
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

        queue.TrackCustom("button_clicked", new Dictionary<string, object?> { ["button"] = "signup" });

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TrackIdentify_Creates_Identify_Event()
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

        queue.TrackIdentify("user-123", new Dictionary<string, object?> { ["plan"] = "premium" });

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Start_And_Stop_Controls_Timer()
    {
        var queue = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: _ => Task.CompletedTask);

        queue.Start();
        Assert.True(queue.IsRunning);

        queue.Stop();
        Assert.False(queue.IsRunning);
    }

    [Fact]
    public async Task Events_Requeued_On_Flush_Failure()
    {
        var failCount = 0;
        var queue = new EventQueue(
            batchSize: 10,
            flushInterval: TimeSpan.FromSeconds(30),
            onFlush: _ =>
            {
                failCount++;
                throw new Exception("Flush failed");
            });

        queue.Enqueue(new AnalyticsEvent { EventType = EventType.Custom });

        try
        {
            await queue.FlushAsync();
        }
        catch
        {
            // Expected
        }

        Assert.Equal(1, queue.Count);
    }
}
