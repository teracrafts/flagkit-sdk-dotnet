using FlagKit.Errors;
using FlagKit.Types;

namespace FlagKit;

/// <summary>
/// Static factory for FlagKit SDK with singleton pattern.
/// Provides convenience methods that delegate to the singleton instance.
/// </summary>
public static class FlagKit
{
    private static readonly object Lock = new();
    private static FlagKitClient? _instance;

    /// <summary>
    /// Gets the current client instance.
    /// </summary>
    /// <exception cref="FlagKitException">Thrown when SDK is not initialized.</exception>
    public static FlagKitClient Instance
    {
        get
        {
            lock (Lock)
            {
                if (_instance == null)
                {
                    throw FlagKitException.ConfigError(
                        ErrorCode.SdkNotInitialized,
                        "SDK not initialized. Call FlagKit.Initialize() first.");
                }
                return _instance;
            }
        }
    }

    /// <summary>
    /// Gets whether the SDK is initialized.
    /// </summary>
    public static bool IsInitialized
    {
        get
        {
            lock (Lock)
            {
                return _instance != null;
            }
        }
    }

    /// <summary>
    /// Gets whether the SDK is ready (initialized and can evaluate flags).
    /// </summary>
    public static bool IsReady
    {
        get
        {
            lock (Lock)
            {
                return _instance?.IsReady ?? false;
            }
        }
    }

    /// <summary>
    /// Initializes the FlagKit SDK with the given options.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <returns>The initialized client.</returns>
    public static FlagKitClient Initialize(FlagKitOptions options)
    {
        lock (Lock)
        {
            if (_instance != null)
            {
                throw FlagKitException.ConfigError(
                    ErrorCode.SdkAlreadyInitialized,
                    "SDK already initialized. Call FlagKit.Close() first to reinitialize.");
            }

            _instance = new FlagKitClient(options);
            return _instance;
        }
    }

    /// <summary>
    /// Initializes the FlagKit SDK with the given API key and optional configuration.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The initialized client.</returns>
    public static FlagKitClient Initialize(string apiKey, Action<FlagKitOptions.Builder>? configure = null)
    {
        var builder = FlagKitOptions.CreateBuilder(apiKey);
        configure?.Invoke(builder);
        return Initialize(builder.Build());
    }

    /// <summary>
    /// Initializes and starts the FlagKit SDK.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initialized and started client.</returns>
    public static async Task<FlagKitClient> InitializeAndStartAsync(
        FlagKitOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = Initialize(options);
        await client.InitializeAsync(cancellationToken);
        return client;
    }

    /// <summary>
    /// Initializes and starts the FlagKit SDK with the given API key.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initialized and started client.</returns>
    public static async Task<FlagKitClient> InitializeAndStartAsync(
        string apiKey,
        Action<FlagKitOptions.Builder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var client = Initialize(apiKey, configure);
        await client.InitializeAsync(cancellationToken);
        return client;
    }

    /// <summary>
    /// Closes the SDK and releases resources.
    /// </summary>
    public static void Close()
    {
        lock (Lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }

    /// <summary>
    /// Closes the SDK asynchronously, flushing any pending events.
    /// </summary>
    public static async Task CloseAsync()
    {
        FlagKitClient? instance;
        lock (Lock)
        {
            instance = _instance;
            _instance = null;
        }

        if (instance != null)
        {
            await instance.DisposeAsync();
        }
    }

    // Convenience methods that delegate to Instance

    /// <summary>
    /// Identifies a user with optional attributes.
    /// </summary>
    public static void Identify(string userId, Dictionary<string, object?>? attributes = null)
        => Instance.Identify(userId, attributes);

    /// <summary>
    /// Sets the global evaluation context.
    /// </summary>
    public static void SetContext(EvaluationContext context)
        => Instance.SetContext(context);

    /// <summary>
    /// Gets the current global evaluation context.
    /// </summary>
    public static EvaluationContext GetContext()
        => Instance.GetContext();

    /// <summary>
    /// Clears the global evaluation context.
    /// </summary>
    public static void ClearContext()
        => Instance.ClearContext();

    /// <summary>
    /// Resets the context to anonymous state.
    /// </summary>
    public static void Reset()
        => Instance.Reset();

    /// <summary>
    /// Evaluates a flag and returns the result.
    /// </summary>
    public static EvaluationResult Evaluate(string flagKey, EvaluationContext? context = null)
        => Instance.Evaluate(flagKey, context);

    /// <summary>
    /// Evaluates a flag asynchronously.
    /// </summary>
    public static Task<EvaluationResult> EvaluateAsync(
        string flagKey,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => Instance.EvaluateAsync(flagKey, context, cancellationToken);

    /// <summary>
    /// Evaluates all flags and returns results.
    /// </summary>
    public static Task<Dictionary<string, EvaluationResult>> EvaluateAllAsync(
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => Instance.EvaluateAllAsync(context, cancellationToken);

    /// <summary>
    /// Evaluates multiple flags in a batch.
    /// </summary>
    public static Task<Dictionary<string, EvaluationResult>> EvaluateBatchAsync(
        IEnumerable<string> flagKeys,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default)
        => Instance.EvaluateBatchAsync(flagKeys, context, cancellationToken);

    /// <summary>
    /// Gets a boolean flag value.
    /// </summary>
    public static bool GetBooleanValue(string flagKey, bool defaultValue, EvaluationContext? context = null)
        => Instance.GetBooleanValue(flagKey, defaultValue, context);

    /// <summary>
    /// Gets a string flag value.
    /// </summary>
    public static string GetStringValue(string flagKey, string defaultValue, EvaluationContext? context = null)
        => Instance.GetStringValue(flagKey, defaultValue, context);

    /// <summary>
    /// Gets a numeric flag value.
    /// </summary>
    public static double GetNumberValue(string flagKey, double defaultValue, EvaluationContext? context = null)
        => Instance.GetNumberValue(flagKey, defaultValue, context);

    /// <summary>
    /// Gets an integer flag value.
    /// </summary>
    public static long GetIntValue(string flagKey, long defaultValue, EvaluationContext? context = null)
        => Instance.GetIntValue(flagKey, defaultValue, context);

    /// <summary>
    /// Gets a JSON flag value.
    /// </summary>
    public static Dictionary<string, object?>? GetJsonValue(
        string flagKey,
        Dictionary<string, object?>? defaultValue,
        EvaluationContext? context = null)
        => Instance.GetJsonValue(flagKey, defaultValue, context);

    /// <summary>
    /// Gets a typed JSON flag value.
    /// </summary>
    public static T GetJsonValue<T>(string flagKey, T defaultValue, EvaluationContext? context = null)
        => Instance.GetJsonValue(flagKey, defaultValue, context);

    /// <summary>
    /// Checks if a flag exists in the cache.
    /// </summary>
    public static bool HasFlag(string flagKey)
        => Instance.HasFlag(flagKey);

    /// <summary>
    /// Gets all flag keys currently in cache.
    /// </summary>
    public static IReadOnlyList<string> GetAllFlagKeys()
        => Instance.GetAllFlagKeys();

    /// <summary>
    /// Gets all cached flags.
    /// </summary>
    public static IReadOnlyDictionary<string, FlagState> GetAllFlags()
        => Instance.GetAllFlags();

    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    public static void Track(string eventType, Dictionary<string, object?>? data = null)
        => Instance.Track(eventType, data);

    /// <summary>
    /// Flushes pending events.
    /// </summary>
    public static Task FlushAsync()
        => Instance.FlushAsync();

    /// <summary>
    /// Forces a refresh of all flags from the server.
    /// </summary>
    public static Task RefreshAsync(CancellationToken cancellationToken = default)
        => Instance.RefreshAsync(cancellationToken);

    /// <summary>
    /// Waits for the SDK to be ready.
    /// </summary>
    public static Task WaitForReadyAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => Instance.WaitForReadyAsync(timeout, cancellationToken);
}
