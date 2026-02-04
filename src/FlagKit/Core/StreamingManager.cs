using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlagKit.Http;
using FlagKit.Types;

namespace FlagKit.Core;

/// <summary>
/// Stream event types from server.
/// </summary>
public enum StreamEventType
{
    [JsonPropertyName("flag_updated")]
    FlagUpdated,

    [JsonPropertyName("flag_deleted")]
    FlagDeleted,

    [JsonPropertyName("flags_reset")]
    FlagsReset,

    [JsonPropertyName("heartbeat")]
    Heartbeat,

    [JsonPropertyName("error")]
    Error
}

/// <summary>
/// SSE error codes from server.
/// </summary>
public enum StreamErrorCode
{
    /// <summary>
    /// Token is invalid, re-authenticate completely.
    /// </summary>
    [JsonPropertyName("TOKEN_INVALID")]
    TokenInvalid,

    /// <summary>
    /// Token has expired, refresh token and reconnect.
    /// </summary>
    [JsonPropertyName("TOKEN_EXPIRED")]
    TokenExpired,

    /// <summary>
    /// Subscription is suspended, notify user and fall back to cached values.
    /// </summary>
    [JsonPropertyName("SUBSCRIPTION_SUSPENDED")]
    SubscriptionSuspended,

    /// <summary>
    /// Connection limit reached, implement backoff or close other connections.
    /// </summary>
    [JsonPropertyName("CONNECTION_LIMIT")]
    ConnectionLimit,

    /// <summary>
    /// Streaming service unavailable, fall back to polling.
    /// </summary>
    [JsonPropertyName("STREAMING_UNAVAILABLE")]
    StreamingUnavailable
}

/// <summary>
/// SSE error event data structure.
/// </summary>
public record StreamErrorData
{
    /// <summary>
    /// The error code indicating the type of error.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>
/// Response from the stream token endpoint.
/// </summary>
public record StreamTokenResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; init; }
}

/// <summary>
/// Connection states for streaming.
/// </summary>
public enum StreamingState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Configuration for streaming.
/// </summary>
public record StreamingConfig
{
    /// <summary>
    /// Whether streaming is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Reconnection interval in milliseconds. Default: 3000.
    /// </summary>
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// Maximum reconnection attempts before fallback. Default: 3.
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>
    /// Expected heartbeat interval in milliseconds. Default: 30000.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMilliseconds(30000);
}

/// <summary>
/// Manages Server-Sent Events (SSE) connection for real-time flag updates.
///
/// Security: Uses token exchange pattern to avoid exposing API keys in URLs.
/// 1. Fetches short-lived token via POST with API key in header
/// 2. Connects to SSE endpoint with disposable token in URL
///
/// Features:
/// - Secure token-based authentication
/// - Automatic token refresh before expiry
/// - Automatic reconnection with exponential backoff
/// - Graceful degradation to polling after max failures
/// - Heartbeat monitoring for connection health
/// - Event parsing and dispatch
/// </summary>
public class StreamingManager : IDisposable, IAsyncDisposable
{
    private readonly string _baseUrl;
    private readonly Func<string> _getApiKey;
    private readonly StreamingConfig _config;
    private readonly Action<FlagState> _onFlagUpdate;
    private readonly Action<string> _onFlagDelete;
    private readonly Action<List<FlagState>> _onFlagsReset;
    private readonly Action _onFallbackToPolling;
    private readonly Action<string>? _onSubscriptionError;
    private readonly Action? _onConnectionLimitError;

    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private StreamingState _state = StreamingState.Disconnected;
    private int _consecutiveFailures;
    private DateTime _lastHeartbeat;
    private CancellationTokenSource? _connectionCts;
    private Timer? _heartbeatTimer;
    private Timer? _tokenRefreshTimer;
    private Timer? _retryStreamingTimer;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Creates a new streaming manager.
    /// </summary>
    /// <param name="baseUrl">Base URL for API endpoints.</param>
    /// <param name="getApiKey">Function to get the current API key (supports key rotation).</param>
    /// <param name="config">Streaming configuration.</param>
    /// <param name="onFlagUpdate">Callback when a flag is updated.</param>
    /// <param name="onFlagDelete">Callback when a flag is deleted.</param>
    /// <param name="onFlagsReset">Callback when all flags are reset.</param>
    /// <param name="onFallbackToPolling">Callback when streaming fails and falls back to polling.</param>
    /// <param name="onSubscriptionError">Callback when subscription error occurs (e.g., suspended).</param>
    /// <param name="onConnectionLimitError">Callback when connection limit is reached.</param>
    public StreamingManager(
        string baseUrl,
        Func<string> getApiKey,
        StreamingConfig config,
        Action<FlagState> onFlagUpdate,
        Action<string> onFlagDelete,
        Action<List<FlagState>> onFlagsReset,
        Action onFallbackToPolling,
        Action<string>? onSubscriptionError = null,
        Action? onConnectionLimitError = null)
    {
        _baseUrl = baseUrl;
        _getApiKey = getApiKey;
        _config = config;
        _onFlagUpdate = onFlagUpdate;
        _onFlagDelete = onFlagDelete;
        _onFlagsReset = onFlagsReset;
        _onFallbackToPolling = onFallbackToPolling;
        _onSubscriptionError = onSubscriptionError;
        _onConnectionLimitError = onConnectionLimitError;

        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan // SSE connections are long-lived
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public StreamingState State => _state;

    /// <summary>
    /// Gets whether streaming is connected.
    /// </summary>
    public bool IsConnected => _state == StreamingState.Connected;

    /// <summary>
    /// Start streaming connection.
    /// </summary>
    public void Connect()
    {
        if (_state == StreamingState.Connected || _state == StreamingState.Connecting)
        {
            return;
        }

        SetState(StreamingState.Connecting);
        _ = InitiateConnectionAsync();
    }

    /// <summary>
    /// Stop streaming connection.
    /// </summary>
    public void Disconnect()
    {
        Cleanup();
        SetState(StreamingState.Disconnected);
        _consecutiveFailures = 0;
    }

    /// <summary>
    /// Retry streaming connection (called periodically after fallback to polling).
    /// </summary>
    public void RetryConnection()
    {
        if (_state == StreamingState.Connected || _state == StreamingState.Connecting)
        {
            return;
        }

        _consecutiveFailures = 0;
        Connect();
    }

    /// <summary>
    /// Initiate connection by first fetching a stream token.
    /// </summary>
    private async Task InitiateConnectionAsync()
    {
        try
        {
            // Step 1: Fetch short-lived stream token
            var tokenResponse = await FetchStreamTokenAsync(_disposalCts.Token);

            // Step 2: Schedule token refresh before expiry (refresh at 80% of TTL)
            ScheduleTokenRefresh(TimeSpan.FromSeconds(tokenResponse.ExpiresIn * 0.8));

            // Step 3: Create SSE connection with token
            await CreateConnectionAsync(tokenResponse.Token, _disposalCts.Token);
        }
        catch (Exception)
        {
            HandleConnectionFailure();
        }
    }

    /// <summary>
    /// Fetch a short-lived stream token from the API.
    /// Token is fetched via POST with API key in header (secure).
    /// </summary>
    private async Task<StreamTokenResponse> FetchStreamTokenAsync(CancellationToken cancellationToken)
    {
        var tokenUrl = $"{_baseUrl}/sdk/stream/token";

        var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Headers.Add("X-API-Key", _getApiKey());
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<StreamTokenResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize stream token response");
    }

    /// <summary>
    /// Schedule token refresh before expiry.
    /// </summary>
    private void ScheduleTokenRefresh(TimeSpan delay)
    {
        ClearTokenRefreshTimer();

        _tokenRefreshTimer = new Timer(
            async _ =>
            {
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = null;

                try
                {
                    var tokenResponse = await FetchStreamTokenAsync(_disposalCts.Token);

                    // Schedule next refresh
                    ScheduleTokenRefresh(TimeSpan.FromSeconds(tokenResponse.ExpiresIn * 0.8));
                }
                catch
                {
                    // Token refresh failed, force reconnection
                    Cleanup();
                    Connect();
                }
            },
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Clear token refresh timer.
    /// </summary>
    private void ClearTokenRefreshTimer()
    {
        _tokenRefreshTimer?.Dispose();
        _tokenRefreshTimer = null;
    }

    /// <summary>
    /// Create SSE connection with token.
    /// </summary>
    private async Task CreateConnectionAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            // Build URL with short-lived token (NOT the API key)
            var streamUrl = $"{_baseUrl}/sdk/stream?token={Uri.EscapeDataString(token)}";

            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            using var response = await _httpClient.GetAsync(
                streamUrl,
                HttpCompletionOption.ResponseHeadersRead,
                _connectionCts.Token);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(_connectionCts.Token);
            using var reader = new StreamReader(stream);

            HandleOpen();

            // Read SSE events
            await ReadEventsAsync(reader, _connectionCts.Token);
        }
        catch (OperationCanceledException) when (_disposed || _connectionCts?.IsCancellationRequested == true)
        {
            // Normal cancellation, ignore
        }
        catch
        {
            HandleConnectionFailure();
        }
    }

    /// <summary>
    /// Read and process SSE events from the stream.
    /// </summary>
    private async Task ReadEventsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        string? eventType = null;
        var dataBuilder = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line == null)
            {
                break;
            }

            // Empty line means end of event
            if (string.IsNullOrEmpty(line))
            {
                if (eventType != null && dataBuilder.Length > 0)
                {
                    ProcessEvent(eventType, dataBuilder.ToString());
                    eventType = null;
                    dataBuilder.Clear();
                }
                continue;
            }

            // Parse SSE format
            if (line.StartsWith("event:"))
            {
                eventType = line[6..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                dataBuilder.Append(line[5..].Trim());
            }
        }

        // Connection closed, handle as failure if we were connected
        if (_state == StreamingState.Connected)
        {
            HandleConnectionFailure();
        }
    }

    /// <summary>
    /// Process a parsed SSE event.
    /// </summary>
    private void ProcessEvent(string eventType, string data)
    {
        try
        {
            switch (eventType)
            {
                case "flag_updated":
                    var flag = JsonSerializer.Deserialize<FlagState>(data, JsonOptions);
                    if (flag != null)
                    {
                        _onFlagUpdate(flag);
                    }
                    break;

                case "flag_deleted":
                    var deleteData = JsonSerializer.Deserialize<Dictionary<string, string>>(data, JsonOptions);
                    if (deleteData?.TryGetValue("key", out var key) == true)
                    {
                        _onFlagDelete(key);
                    }
                    break;

                case "flags_reset":
                    var flags = JsonSerializer.Deserialize<List<FlagState>>(data, JsonOptions);
                    if (flags != null)
                    {
                        _onFlagsReset(flags);
                    }
                    break;

                case "heartbeat":
                    HandleHeartbeat();
                    break;

                case "error":
                    HandleStreamError(data);
                    break;
            }
        }
        catch
        {
            // Failed to parse event, ignore
        }
    }

    /// <summary>
    /// Handle SSE error event from server.
    /// These are application-level errors sent as SSE events, not connection errors.
    ///
    /// Error codes:
    /// - TOKEN_INVALID: Re-authenticate completely
    /// - TOKEN_EXPIRED: Refresh token and reconnect
    /// - SUBSCRIPTION_SUSPENDED: Notify user, fall back to cached values
    /// - CONNECTION_LIMIT: Implement backoff or close other connections
    /// - STREAMING_UNAVAILABLE: Fall back to polling
    /// </summary>
    /// <param name="data">The JSON error data from the SSE event.</param>
    private void HandleStreamError(string data)
    {
        try
        {
            var errorData = JsonSerializer.Deserialize<StreamErrorData>(data, JsonOptions);
            if (errorData == null)
            {
                return;
            }

            Console.WriteLine($"[FlagKit] SSE error event received: {errorData.Code} - {errorData.Message}");

            switch (errorData.Code.ToUpperInvariant())
            {
                case "TOKEN_EXPIRED":
                    // Token expired, refresh and reconnect
                    Console.WriteLine("[FlagKit] Stream token expired, refreshing...");
                    Cleanup();
                    Connect(); // Will fetch new token
                    break;

                case "TOKEN_INVALID":
                    // Token is invalid, need full re-authentication
                    Console.WriteLine("[FlagKit] Stream token invalid, re-authenticating...");
                    Cleanup();
                    Connect(); // Will fetch new token
                    break;

                case "SUBSCRIPTION_SUSPENDED":
                    // Subscription issue - notify and fall back
                    Console.WriteLine($"[FlagKit] Subscription suspended: {errorData.Message}");
                    _onSubscriptionError?.Invoke(errorData.Message);
                    Cleanup();
                    SetState(StreamingState.Failed);
                    _onFallbackToPolling();
                    break;

                case "CONNECTION_LIMIT":
                    // Too many connections - implement backoff
                    Console.WriteLine("[FlagKit] Connection limit reached, backing off...");
                    _onConnectionLimitError?.Invoke();
                    HandleConnectionFailure();
                    break;

                case "STREAMING_UNAVAILABLE":
                    // Streaming not available - fall back to polling
                    Console.WriteLine("[FlagKit] Streaming service unavailable, falling back to polling");
                    Cleanup();
                    SetState(StreamingState.Failed);
                    _onFallbackToPolling();
                    break;

                default:
                    Console.WriteLine($"[FlagKit] Unknown stream error code: {errorData.Code}");
                    HandleConnectionFailure();
                    break;
            }
        }
        catch
        {
            // Failed to parse error data, treat as connection failure
            Console.WriteLine("[FlagKit] Failed to parse stream error data");
            HandleConnectionFailure();
        }
    }

    /// <summary>
    /// Handle successful connection.
    /// </summary>
    private void HandleOpen()
    {
        SetState(StreamingState.Connected);
        _consecutiveFailures = 0;
        _lastHeartbeat = DateTime.UtcNow;
        StartHeartbeatMonitor();
    }

    /// <summary>
    /// Handle heartbeat event.
    /// </summary>
    private void HandleHeartbeat()
    {
        _lastHeartbeat = DateTime.UtcNow;
    }

    /// <summary>
    /// Handle connection failure.
    /// </summary>
    private void HandleConnectionFailure()
    {
        Cleanup();
        _consecutiveFailures++;

        if (_consecutiveFailures >= _config.MaxReconnectAttempts)
        {
            SetState(StreamingState.Failed);
            _onFallbackToPolling();

            // Schedule retry of streaming in 5 minutes
            ScheduleStreamingRetry();
        }
        else
        {
            SetState(StreamingState.Reconnecting);
            ScheduleReconnect();
        }
    }

    /// <summary>
    /// Schedule reconnection attempt.
    /// </summary>
    private void ScheduleReconnect()
    {
        var delay = GetReconnectDelay();

        _ = Task.Delay(delay, _disposalCts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Connect();
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Get reconnection delay with exponential backoff.
    /// </summary>
    private TimeSpan GetReconnectDelay()
    {
        var baseDelay = _config.ReconnectInterval.TotalMilliseconds;
        var backoff = Math.Pow(2, _consecutiveFailures - 1);
        var delay = baseDelay * backoff;

        // Cap at 30 seconds
        return TimeSpan.FromMilliseconds(Math.Min(delay, 30000));
    }

    /// <summary>
    /// Schedule retry of streaming after fallback to polling.
    /// </summary>
    private void ScheduleStreamingRetry()
    {
        // Retry streaming every 5 minutes
        var retryInterval = TimeSpan.FromMinutes(5);

        _retryStreamingTimer = new Timer(
            _ =>
            {
                _retryStreamingTimer?.Dispose();
                _retryStreamingTimer = null;
                RetryConnection();
            },
            null,
            retryInterval,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Start heartbeat monitoring.
    /// </summary>
    private void StartHeartbeatMonitor()
    {
        StopHeartbeatMonitor();

        // Check heartbeat at 1.5x the expected interval
        var checkInterval = TimeSpan.FromMilliseconds(_config.HeartbeatInterval.TotalMilliseconds * 1.5);

        _heartbeatTimer = new Timer(
            _ =>
            {
                var timeSinceLastHeartbeat = DateTime.UtcNow - _lastHeartbeat;

                if (timeSinceLastHeartbeat > TimeSpan.FromMilliseconds(_config.HeartbeatInterval.TotalMilliseconds * 2))
                {
                    // Heartbeat timeout, reconnect
                    HandleConnectionFailure();
                }
            },
            null,
            checkInterval,
            checkInterval);
    }

    /// <summary>
    /// Stop heartbeat monitoring.
    /// </summary>
    private void StopHeartbeatMonitor()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>
    /// Set connection state.
    /// </summary>
    private void SetState(StreamingState state)
    {
        _state = state;
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    private void Cleanup()
    {
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;

        StopHeartbeatMonitor();
        ClearTokenRefreshTimer();

        _retryStreamingTimer?.Dispose();
        _retryStreamingTimer = null;
    }

    /// <summary>
    /// Disposes the streaming manager.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the streaming manager asynchronously.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Protected dispose implementation.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Cleanup();
            _disposalCts.Cancel();
            _disposalCts.Dispose();
            _connectionLock.Dispose();
            _httpClient.Dispose();
        }

        _disposed = true;
    }
}
