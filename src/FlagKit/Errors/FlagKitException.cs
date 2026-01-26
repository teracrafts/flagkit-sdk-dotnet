namespace FlagKit.Errors;

/// <summary>
/// Exception for FlagKit SDK errors.
/// </summary>
public class FlagKitException : Exception
{
    private static ErrorSanitizationConfig _sanitizationConfig = new();

    /// <summary>
    /// Configures error message sanitization globally for all FlagKitExceptions.
    /// </summary>
    /// <param name="config">The sanitization configuration to use.</param>
    public static void ConfigureSanitization(ErrorSanitizationConfig config)
    {
        _sanitizationConfig = config ?? new ErrorSanitizationConfig();
    }

    /// <summary>
    /// Gets the current sanitization configuration.
    /// </summary>
    public static ErrorSanitizationConfig SanitizationConfig => _sanitizationConfig;

    /// <summary>
    /// The original unsanitized message, if PreserveOriginal is enabled.
    /// </summary>
    public string? OriginalMessage { get; }

    public ErrorCode ErrorCode { get; }
    public ErrorCode Code => ErrorCode;
    public bool IsRecoverable => ErrorCode.IsRecoverable();

    public bool IsConfigError => ErrorCode is
        ErrorCode.ConfigInvalidUrl or
        ErrorCode.ConfigInvalidInterval or
        ErrorCode.ConfigMissingRequired or
        ErrorCode.ConfigInvalidApiKey or
        ErrorCode.ConfigInvalidBaseUrl or
        ErrorCode.ConfigInvalidPollingInterval or
        ErrorCode.ConfigInvalidCacheTtl;

    public bool IsNetworkError => ErrorCode is
        ErrorCode.NetworkError or
        ErrorCode.NetworkTimeout or
        ErrorCode.NetworkRetryLimit or
        ErrorCode.HttpBadRequest or
        ErrorCode.HttpUnauthorized or
        ErrorCode.HttpForbidden or
        ErrorCode.HttpNotFound or
        ErrorCode.HttpRateLimited or
        ErrorCode.HttpServerError or
        ErrorCode.HttpTimeout or
        ErrorCode.HttpNetworkError or
        ErrorCode.HttpInvalidResponse or
        ErrorCode.HttpCircuitOpen;

    public bool IsEvaluationError => ErrorCode is
        ErrorCode.EvalFlagNotFound or
        ErrorCode.EvalTypeMismatch or
        ErrorCode.EvalInvalidKey or
        ErrorCode.EvalInvalidValue or
        ErrorCode.EvalDisabled or
        ErrorCode.EvalError or
        ErrorCode.EvalContextError or
        ErrorCode.EvalDefaultUsed or
        ErrorCode.EvalStaleValue or
        ErrorCode.EvalCacheMiss or
        ErrorCode.EvalNetworkError or
        ErrorCode.EvalParseError or
        ErrorCode.EvalTimeoutError;

    public bool IsInternalError => ErrorCode is
        ErrorCode.InitFailed or
        ErrorCode.SdkNotInitialized or
        ErrorCode.SdkAlreadyInitialized or
        ErrorCode.SdkNotReady;

    public FlagKitException(ErrorCode errorCode, string message, Exception? innerException = null)
        : base(FormatMessage(errorCode, message), innerException)
    {
        ErrorCode = errorCode;
        if (_sanitizationConfig.PreserveOriginal && _sanitizationConfig.Enabled)
        {
            OriginalMessage = $"[{errorCode.ToCode()}] {message}";
        }
    }

    private static string FormatMessage(ErrorCode errorCode, string message)
    {
        var sanitizedMessage = ErrorSanitizer.Sanitize(message, _sanitizationConfig.Enabled);
        return $"[{errorCode.ToCode()}] {sanitizedMessage}";
    }

    public static FlagKitException InitError(string message) =>
        new(ErrorCode.InitFailed, message);

    public static FlagKitException AuthError(ErrorCode code, string message) =>
        new(code, message);

    public static FlagKitException NetworkError(string message, Exception? innerException = null) =>
        new(ErrorCode.NetworkError, message, innerException);

    public static FlagKitException NetworkError(ErrorCode code, string message, Exception? innerException = null) =>
        new(code, message, innerException);

    public static FlagKitException EvalError(ErrorCode code, string message) =>
        new(code, message);

    public static FlagKitException EvaluationError(ErrorCode code, string message) =>
        new(code, message);

    public static FlagKitException ConfigError(ErrorCode code, string message) =>
        new(code, message);

    public static FlagKitException InternalError(ErrorCode code, string message, Exception? innerException = null) =>
        new(code, message, innerException);
}
