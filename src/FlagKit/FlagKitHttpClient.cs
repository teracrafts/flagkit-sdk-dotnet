using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlagKit;

/// <summary>
/// HTTP client with retry logic and circuit breaker.
/// </summary>
public class FlagKitHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly FlagKitOptions _options;
    private readonly Random _random = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public FlagKitHttpClient(FlagKitOptions options)
    {
        _options = options;
        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold,
            options.CircuitBreakerResetTimeout);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = options.Timeout
        };

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"FlagKit-DotNet/{GetVersion()}");
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.GetAsync(path, cancellationToken);
            return await HandleResponseAsync<T>(response, cancellationToken);
        }, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
            return await HandleResponseAsync<TResponse>(response, cancellationToken);
        }, cancellationToken);
    }

    public async Task PostAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return true;
        }, cancellationToken);
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
