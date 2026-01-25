using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlagKit.Errors;
using FlagKit.Utils;

namespace FlagKit.Http;

/// <summary>
/// HTTP client with retry logic, circuit breaker, request signing, and key rotation.
/// </summary>
public class FlagKitHttpClient : IDisposable
{
    internal const string DefaultBaseUrl = "https://api.flagkit.dev/api/v1";

    private readonly HttpClient _httpClient;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly FlagKitOptions _options;
    private readonly Random _random = new();
    private bool _disposed;
    private bool _usingSecondaryKey = false;
    private readonly object _keyRotationLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Returns the base URL for the given local port, or the default production URL.
    /// </summary>
    public static string GetBaseUrl(int? localPort) =>
        localPort.HasValue ? $"http://localhost:{localPort.Value}/api/v1" : DefaultBaseUrl;

    public FlagKitHttpClient(FlagKitOptions options)
    {
        _options = options;
        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold,
            options.CircuitBreakerResetTimeout);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(GetBaseUrl(options.LocalPort)),
            Timeout = options.Timeout
        };

        // Note: API key is now added per-request to support key rotation
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"FlagKit-DotNet/{GetVersion()}");
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-API-Key", apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return await HandleResponseAsync<T>(response, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the currently active API key (primary or secondary if rotated).
    /// </summary>
    public string CurrentApiKey
    {
        get
        {
            lock (_keyRotationLock)
            {
                return _usingSecondaryKey && !string.IsNullOrEmpty(_options.SecondaryApiKey)
                    ? _options.SecondaryApiKey
                    : _options.ApiKey;
            }
        }
    }

    /// <summary>
    /// Gets whether the client is currently using the secondary API key.
    /// </summary>
    public bool IsUsingSecondaryKey
    {
        get
        {
            lock (_keyRotationLock)
            {
                return _usingSecondaryKey;
            }
        }
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = CreateSignedPostRequest(path, body, apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return await HandleResponseAsync<TResponse>(response, cancellationToken);
        }, cancellationToken);
    }

    public async Task PostAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAndKeyRotationAsync(async (apiKey) =>
        {
            var request = CreateSignedPostRequest(path, body, apiKey);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private HttpRequestMessage CreateSignedPostRequest<TRequest>(
        string path,
        TRequest body,
        string apiKey)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        // Add API key header
        request.Headers.Add("X-API-Key", apiKey);

        // Add request signing if enabled
        if (_options.EnableRequestSigning)
        {
            var (signature, timestamp) = Security.CreateRequestSignature(jsonBody, apiKey);
            request.Headers.Add("X-Signature", signature);
            request.Headers.Add("X-Timestamp", timestamp.ToString());
            request.Headers.Add("X-Key-Id", Security.GetKeyId(apiKey));
        }

        return request;
    }

    private async Task<T> ExecuteWithRetryAndKeyRotationAsync<T>(
        Func<string, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteWithRetryAsync(
                () => action(CurrentApiKey),
                cancellationToken);
        }
        catch (FlagKitException ex) when (ex.Code == ErrorCode.HttpUnauthorized)
        {
            // Try key rotation if we have a secondary key and haven't already rotated
            if (!string.IsNullOrEmpty(_options.SecondaryApiKey))
            {
                bool shouldRetry;
                lock (_keyRotationLock)
                {
                    if (!_usingSecondaryKey)
                    {
                        _usingSecondaryKey = true;
                        shouldRetry = true;
                    }
                    else
                    {
                        shouldRetry = false;
                    }
                }

                if (shouldRetry)
                {
                    // Retry with the secondary key
                    return await ExecuteWithRetryAsync(
                        () => action(CurrentApiKey),
                        cancellationToken);
                }
            }

            // No secondary key or already tried - rethrow
            throw;
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            Exception? lastException = null;

            for (int attempt = 0; attempt <= _options.RetryAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (IsRetryable(ex) && attempt < _options.RetryAttempts)
                {
                    lastException = ex;
                    var delay = CalculateBackoff(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw lastException ?? new InvalidOperationException("Retry failed without exception");
        });
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            TaskCanceledException { InnerException: TimeoutException } => true,
            FlagKitException fke => fke.Code switch
            {
                ErrorCode.HttpTimeout => true,
                ErrorCode.HttpNetworkError => true,
                ErrorCode.HttpServerError => true,
                _ => false
            },
            _ => false
        };
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        const double baseDelay = 1000;
        const double maxDelay = 30000;
        const double multiplier = 2.0;

        var delay = baseDelay * Math.Pow(multiplier, attempt);
        delay = Math.Min(delay, maxDelay);

        // Add jitter (0-25%)
        var jitter = delay * 0.25 * _random.NextDouble();
        delay += jitter;

        return TimeSpan.FromMilliseconds(delay);
    }

    private static async Task<T> HandleResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
            return result ?? throw FlagKitException.NetworkError(
                ErrorCode.HttpInvalidResponse,
                "Response deserialized to null");
        }
        catch (JsonException ex)
        {
            throw FlagKitException.NetworkError(
                ErrorCode.HttpInvalidResponse,
                $"Failed to deserialize response: {ex.Message}");
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        var (errorCode, category) = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => (ErrorCode.HttpBadRequest, "Client Error"),
            HttpStatusCode.Unauthorized => (ErrorCode.HttpUnauthorized, "Authentication Error"),
            HttpStatusCode.Forbidden => (ErrorCode.HttpForbidden, "Authorization Error"),
            HttpStatusCode.NotFound => (ErrorCode.HttpNotFound, "Not Found"),
            HttpStatusCode.TooManyRequests => (ErrorCode.HttpRateLimited, "Rate Limited"),
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout => (ErrorCode.HttpServerError, "Server Error"),
            _ when statusCode >= 400 && statusCode < 500 => (ErrorCode.HttpBadRequest, "Client Error"),
            _ => (ErrorCode.HttpServerError, "Server Error")
        };

        throw FlagKitException.NetworkError(
            errorCode,
            $"{category}: {statusCode} - {content}");
    }

    private static string GetVersion()
    {
        return typeof(FlagKitHttpClient).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
