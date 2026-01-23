using FlagKit.Errors;
using FlagKit.Http;
using Xunit;

namespace FlagKit.Tests;

public class CircuitBreakerTests
{
    [Fact]
    public void Initial_State_Is_Closed()
    {
        var breaker = new CircuitBreaker();

        Assert.Equal(CircuitState.Closed, breaker.State);
        Assert.True(breaker.IsClosed);
        Assert.True(breaker.CanExecute());
    }

    [Fact]
    public void Opens_After_Threshold_Failures()
    {
        var breaker = new CircuitBreaker(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.True(breaker.IsClosed);

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);
        Assert.False(breaker.CanExecute());
    }

    [Fact]
    public void Success_Resets_Failure_Count()
    {
        var breaker = new CircuitBreaker(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();

        Assert.Equal(0, breaker.FailureCount);
        Assert.True(breaker.IsClosed);
    }

    [Fact]
    public void Transitions_To_HalfOpen_After_Reset_Timeout()
    {
        var breaker = new CircuitBreaker(threshold: 1, resetTimeout: TimeSpan.FromMilliseconds(10));

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);

        Thread.Sleep(20);

        Assert.True(breaker.IsHalfOpen);
        Assert.True(breaker.CanExecute());
    }

    [Fact]
    public void Success_In_HalfOpen_Closes_Circuit()
    {
        var breaker = new CircuitBreaker(threshold: 1, resetTimeout: TimeSpan.FromMilliseconds(10));

        breaker.RecordFailure();
        Thread.Sleep(20);

        breaker.RecordSuccess();

        Assert.True(breaker.IsClosed);
    }

    [Fact]
    public void Failure_In_HalfOpen_Opens_Circuit()
    {
        var breaker = new CircuitBreaker(threshold: 1, resetTimeout: TimeSpan.FromMilliseconds(10));

        breaker.RecordFailure();
        Thread.Sleep(20);

        Assert.True(breaker.IsHalfOpen);
        breaker.RecordFailure();

        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public void Reset_Returns_To_Closed()
    {
        var breaker = new CircuitBreaker(threshold: 1);

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);

        breaker.Reset();

        Assert.True(breaker.IsClosed);
        Assert.Equal(0, breaker.FailureCount);
    }

    [Fact]
    public async Task Execute_Records_Success()
    {
        var breaker = new CircuitBreaker();

        var result = await breaker.ExecuteAsync(async () =>
        {
            await Task.Yield();
            return "success";
        });

        Assert.Equal("success", result);
        Assert.Equal(0, breaker.FailureCount);
    }

    [Fact]
    public async Task Execute_Records_Failure_On_Exception()
    {
        var breaker = new CircuitBreaker(threshold: 5);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await breaker.ExecuteAsync<string>(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("test");
            });
        });

        Assert.Equal(1, breaker.FailureCount);
    }

    [Fact]
    public async Task Execute_Uses_Fallback_When_Open()
    {
        var breaker = new CircuitBreaker(threshold: 1);
        breaker.RecordFailure();

        var result = await breaker.ExecuteAsync(
            async () =>
            {
                await Task.Yield();
                return "primary";
            },
            () => "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public async Task Execute_Throws_When_Open_Without_Fallback()
    {
        var breaker = new CircuitBreaker(threshold: 1);
        breaker.RecordFailure();

        var ex = await Assert.ThrowsAsync<FlagKitException>(async () =>
        {
            await breaker.ExecuteAsync(async () =>
            {
                await Task.Yield();
                return "value";
            });
        });

        Assert.Equal(ErrorCode.HttpCircuitOpen, ex.Code);
    }
}
