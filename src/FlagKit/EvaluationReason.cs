using System.Text.Json.Serialization;

namespace FlagKit;

/// <summary>
/// Reasons for flag evaluation results.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EvaluationReason
{
    Cached,
    Default,
    FlagNotFound,
    NotFound = FlagNotFound,
    Bootstrap,
    Server,
    StaleCache,
    Error,
    Disabled,
    TypeMismatch,
    Offline
}
