using FlagKit.Types;

namespace FlagKit.Core;

/// <summary>
/// Thread-safe manager for global and per-evaluation context.
/// </summary>
public class ContextManager
{
    private readonly object _lock = new();
    private EvaluationContext _globalContext = new();

    /// <summary>
    /// Gets or sets the global context.
    /// </summary>
    public EvaluationContext GlobalContext
    {
        get
        {
            lock (_lock)
            {
                return _globalContext;
            }
        }
    }

    /// <summary>
    /// Sets the global context.
    /// </summary>
    /// <param name="context">The context to set.</param>
    public void SetContext(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_lock)
        {
            _globalContext = context;
        }
    }

    /// <summary>
    /// Gets the current global context.
    /// </summary>
    /// <returns>The current global context.</returns>
    public EvaluationContext GetContext()
    {
        lock (_lock)
        {
            return _globalContext;
        }
    }

    /// <summary>
    /// Clears the global context.
    /// </summary>
    public void ClearContext()
    {
        lock (_lock)
        {
            _globalContext = new EvaluationContext();
        }
    }

    /// <summary>
    /// Identifies a user with optional attributes.
    /// </summary>
    /// <param name="userId">The user ID to identify.</param>
    /// <param name="attributes">Optional additional attributes.</param>
    public void Identify(string userId, Dictionary<string, object?>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(userId));

        lock (_lock)
        {
            _globalContext = _globalContext.WithUserId(userId);

            if (attributes != null)
            {
                _globalContext = _globalContext.WithAttributes(attributes);
            }

            // Mark as not anonymous
            _globalContext = _globalContext.WithAttribute("anonymous", false);
        }
    }

    /// <summary>
    /// Resets the context to anonymous state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _globalContext = new EvaluationContext().WithAttribute("anonymous", true);
        }
    }

    /// <summary>
    /// Resolves the effective context by merging global and evaluation context.
    /// Private attributes are stripped from the result.
    /// </summary>
    /// <param name="evaluationContext">The per-evaluation context to merge.</param>
    /// <returns>The merged context with private attributes stripped.</returns>
    public EvaluationContext ResolveContext(EvaluationContext? evaluationContext = null)
    {
        EvaluationContext global;
        lock (_lock)
        {
            global = _globalContext;
        }

        var merged = global.Merge(evaluationContext);
        return merged.StripPrivateAttributes();
    }

    /// <summary>
    /// Gets the merged context without stripping private attributes.
    /// </summary>
    /// <param name="evaluationContext">The per-evaluation context to merge.</param>
    /// <returns>The merged context.</returns>
    public EvaluationContext GetMergedContext(EvaluationContext? evaluationContext = null)
    {
        EvaluationContext global;
        lock (_lock)
        {
            global = _globalContext;
        }

        return global.Merge(evaluationContext);
    }

    /// <summary>
    /// Checks if a user is identified (has userId and is not anonymous).
    /// </summary>
    /// <returns>True if a user is identified.</returns>
    public bool IsIdentified()
    {
        lock (_lock)
        {
            return !string.IsNullOrEmpty(_globalContext.UserId) &&
                   _globalContext["anonymous"]?.BoolValue != true;
        }
    }

    /// <summary>
    /// Checks if the context is anonymous.
    /// </summary>
    /// <returns>True if anonymous.</returns>
    public bool IsAnonymous()
    {
        lock (_lock)
        {
            return string.IsNullOrEmpty(_globalContext.UserId) ||
                   _globalContext["anonymous"]?.BoolValue == true;
        }
    }

    /// <summary>
    /// Gets the current user ID if identified.
    /// </summary>
    /// <returns>The user ID, or null if not identified.</returns>
    public string? GetUserId()
    {
        lock (_lock)
        {
            return _globalContext.UserId;
        }
    }

    /// <summary>
    /// Updates a single attribute in the global context.
    /// </summary>
    /// <param name="key">The attribute key.</param>
    /// <param name="value">The attribute value.</param>
    public void SetAttribute(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(key));

        lock (_lock)
        {
            _globalContext = _globalContext.WithAttribute(key, value);
        }
    }

    /// <summary>
    /// Gets an attribute value from the global context.
    /// </summary>
    /// <param name="key">The attribute key.</param>
    /// <returns>The attribute value, or null if not found.</returns>
    public FlagValue? GetAttribute(string key)
    {
        lock (_lock)
        {
            return _globalContext[key];
        }
    }

    /// <summary>
    /// Removes an attribute from the global context.
    /// </summary>
    /// <param name="key">The attribute key.</param>
    public void RemoveAttribute(string key)
    {
        lock (_lock)
        {
            var newAttrs = new Dictionary<string, FlagValue>(_globalContext.Attributes);
            newAttrs.Remove(key);
            _globalContext = _globalContext with { Attributes = newAttrs };
        }
    }
}
