# FlagKit .NET SDK

Official .NET SDK for [FlagKit](https://flagkit.dev) feature flag service.

## Requirements

- .NET 8.0 or later

## Installation

```bash
dotnet add package Teracrafts.FlagKit
```

## Features

- **Type-safe evaluation** - Boolean, string, number, and JSON flag types
- **Local caching** - Fast evaluations with configurable TTL and optional encryption
- **Background polling** - Automatic flag updates
- **Event tracking** - Analytics with batching and crash-resilient persistence
- **Resilient** - Circuit breaker, retry with exponential backoff, offline support
- **Thread-safe** - Safe for concurrent async/await operations
- **Security** - PII detection, request signing, bootstrap verification, timing attack protection

## Architecture

The SDK is organized into clean, modular components:

```
FlagKit/
├── FlagKit.cs              # Static factory methods
├── FlagKitClient.cs        # Main client implementation
├── FlagKitOptions.cs       # Configuration options
├── Core/                   # Core components
│   ├── FlagCache.cs        # In-memory cache with TTL
│   ├── ContextManager.cs
│   ├── PollingManager.cs
│   ├── EventQueue.cs       # Event batching
│   └── EventPersistence.cs # Crash-resilient persistence
├── Http/                   # HTTP client, circuit breaker, retry
│   ├── HttpClient.cs
│   └── CircuitBreaker.cs
├── Errors/                 # Error types and codes
│   ├── FlagKitException.cs
│   ├── ErrorCode.cs
│   └── ErrorSanitizer.cs
├── Types/                  # Type definitions
│   ├── EvaluationContext.cs
│   ├── EvaluationResult.cs
│   └── FlagState.cs
└── Utils/                  # Utilities
    └── Security.cs         # PII detection, HMAC signing
```

## Quick Start

```csharp
using FlagKit;

// Initialize the SDK
var client = await FlagKit.InitializeAndStartAsync("sdk_your_api_key");

// Wait for initialization
await client.WaitForReadyAsync();

// Identify user
FlagKit.Identify("user-123", new Dictionary<string, object?>
{
    ["plan"] = "premium",
    ["beta"] = true
});

// Evaluate flags
var darkMode = FlagKit.GetBooleanValue("dark-mode", defaultValue: false);
var theme = FlagKit.GetStringValue("theme", defaultValue: "light");
var maxItems = FlagKit.GetIntValue("max-items", defaultValue: 10);

// Track events
FlagKit.Track("button_clicked", new Dictionary<string, object?>
{
    ["button"] = "signup"
});

// Cleanup when done
FlagKit.Close();
```

## Configuration

```csharp
var client = FlagKit.Initialize("sdk_your_api_key", builder =>
{
    builder
        .PollingInterval(TimeSpan.FromSeconds(60))
        .CacheTtl(TimeSpan.FromMinutes(10))
        .MaxCacheSize(500)
        .CacheEnabled(true)
        .EventBatchSize(20)
        .EventFlushInterval(TimeSpan.FromSeconds(60))
        .EventsEnabled(true)
        .Timeout(TimeSpan.FromSeconds(30))
        .RetryAttempts(5);
});

await client.InitializeAsync();
```

Or using options directly:

```csharp
var options = new FlagKitOptions
{
    ApiKey = "sdk_your_api_key",
    PollingInterval = TimeSpan.FromSeconds(60),
    CacheTtl = TimeSpan.FromMinutes(10),
    MaxCacheSize = 500
};

var client = FlagKit.Initialize(options);
```

## Evaluation Context

Provide context for targeting rules:

```csharp
// Using builder pattern
var context = EvaluationContext.CreateBuilder()
    .UserId("user-123")
    .Attribute("plan", "premium")
    .Attribute("beta", true)
    .Attribute("score", 95.5)
    .Build();

var result = FlagKit.Evaluate("feature-flag", context);

// Using fluent methods
var context = new EvaluationContext()
    .WithUserId("user-123")
    .WithAttribute("plan", "premium")
    .WithAttributes(new Dictionary<string, object?>
    {
        ["region"] = "us-east",
        ["beta"] = true
    });
```

## Flag Evaluation

### Basic Evaluation

```csharp
// Boolean flags
var enabled = FlagKit.GetBooleanValue("feature-enabled", defaultValue: false);

// String flags
var variant = FlagKit.GetStringValue("experiment-variant", defaultValue: "control");

// Number flags
var limit = FlagKit.GetNumberValue("rate-limit", defaultValue: 100.0);
var count = FlagKit.GetIntValue("max-count", defaultValue: 10);

// JSON flags
var config = FlagKit.GetJsonValue("feature-config", defaultValue: null);
```

### Detailed Evaluation

```csharp
var result = FlagKit.Evaluate("feature-flag");

Console.WriteLine($"Flag: {result.FlagKey}");
Console.WriteLine($"Value: {result.Value}");
Console.WriteLine($"Enabled: {result.Enabled}");
Console.WriteLine($"Reason: {result.Reason}");
Console.WriteLine($"Version: {result.Version}");
```

### Async Evaluation

```csharp
// Server-side evaluation with full context
var result = await client.EvaluateAsync("feature-flag", context);
```

## User Identification

```csharp
// Identify user with attributes
FlagKit.Identify("user-123", new Dictionary<string, object?>
{
    ["email"] = "user@example.com",
    ["plan"] = "enterprise",
    ["createdAt"] = DateTime.UtcNow.ToString("O")
});

// Update context
FlagKit.SetContext(new EvaluationContext()
    .WithUserId("user-456")
    .WithAttribute("admin", true));

// Clear context
FlagKit.ClearContext();
```

## Analytics

```csharp
// Track custom events
FlagKit.Track("purchase_completed", new Dictionary<string, object?>
{
    ["amount"] = 99.99,
    ["currency"] = "USD",
    ["product_id"] = "prod-123"
});

// Flush pending events
await FlagKit.FlushAsync();
```

## Bootstrap Data

Initialize with local flag data for instant evaluation:

```csharp
var bootstrap = new Dictionary<string, object>
{
    ["dark-mode"] = true,
    ["theme"] = "dark",
    ["max-items"] = 50
};

var options = FlagKitOptions.CreateBuilder("sdk_your_api_key")
    .Bootstrap(bootstrap)
    .Build();

var client = FlagKit.Initialize(options);
// Flags available immediately from bootstrap
```

## Error Handling

```csharp
try
{
    await client.InitializeAsync();
}
catch (FlagKitException ex) when (ex.IsConfigError)
{
    Console.WriteLine($"Configuration error: {ex.Message}");
}
catch (FlagKitException ex) when (ex.IsNetworkError)
{
    Console.WriteLine($"Network error: {ex.Message}");
}
catch (FlagKitException ex)
{
    Console.WriteLine($"Error [{ex.Code}]: {ex.Message}");
}
```

## Thread Safety

The SDK is thread-safe and can be used from multiple threads:

```csharp
// Safe to call from any thread
var value = FlagKit.GetBooleanValue("feature", false);
FlagKit.Track("event");
FlagKit.Identify("user");
```

## Dependency Injection

```csharp
// In Program.cs or Startup.cs
services.AddSingleton<FlagKitClient>(sp =>
{
    var options = new FlagKitOptions
    {
        ApiKey = configuration["FlagKit:ApiKey"]!
    };
    return FlagKit.Initialize(options);
});

// In your service
public class MyService
{
    private readonly FlagKitClient _flagKit;

    public MyService(FlagKitClient flagKit)
    {
        _flagKit = flagKit;
    }

    public async Task DoSomething()
    {
        if (_flagKit.GetBooleanValue("new-feature", false))
        {
            // New implementation
        }
    }
}
```

## API Reference

### FlagKit (Static Factory)

| Method | Description |
|--------|-------------|
| `Initialize(options)` | Initialize SDK with options |
| `Initialize(apiKey, configure?)` | Initialize SDK with API key |
| `InitializeAndStartAsync(...)` | Initialize and start SDK |
| `Close()` | Close SDK and release resources |
| `Identify(userId, attributes?)` | Set user context |
| `SetContext(context)` | Set evaluation context |
| `ClearContext()` | Clear evaluation context |
| `Evaluate(flagKey, context?)` | Evaluate a flag |
| `GetBooleanValue(...)` | Get boolean flag value |
| `GetStringValue(...)` | Get string flag value |
| `GetNumberValue(...)` | Get number flag value |
| `GetIntValue(...)` | Get integer flag value |
| `GetJsonValue(...)` | Get JSON flag value |
| `GetAllFlags()` | Get all cached flags |
| `Track(eventType, data?)` | Track custom event |
| `FlushAsync()` | Flush pending events |
| `WaitForReadyAsync(timeout?)` | Wait for initialization |

### FlagKitOptions

| Property | Default | Description |
|----------|---------|-------------|
| `ApiKey` | (required) | API key for authentication |
| `PollingInterval` | 30 seconds | Polling interval |
| `CacheTtl` | 5 minutes | Cache time-to-live |
| `MaxCacheSize` | 1000 | Maximum cache entries |
| `CacheEnabled` | true | Enable caching |
| `EventBatchSize` | 10 | Events per batch |
| `EventFlushInterval` | 30 seconds | Event flush interval |
| `EventsEnabled` | true | Enable event tracking |
| `Timeout` | 10 seconds | HTTP timeout |
| `RetryAttempts` | 3 | Max retry attempts |
| `Bootstrap` | null | Initial flag data |
| `LocalPort` | null | Local dev server port (uses `http://localhost:{port}/api/v1`) |

## Local Development

For local development, use the `LocalPort` option to connect to a local FlagKit server:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_your_api_key")
    .LocalPort(8200)  // Uses http://localhost:8200/api/v1
    .Build();

var client = FlagKit.Initialize(options);
```

## Security Features

### PII Detection

The SDK can detect and warn about potential PII (Personally Identifiable Information) in contexts and events:

```csharp
// Enable strict PII mode - throws exceptions instead of warnings
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .StrictPIIMode(true)
    .Build();

// Attributes containing PII will throw FlagKitException
try
{
    FlagKit.Identify("user-123", new Dictionary<string, object?>
    {
        ["email"] = "user@example.com"  // PII detected!
    });
}
catch (FlagKitException ex)
{
    Console.WriteLine($"PII error: {ex.Message}");
}

// Use PrivateAttributes to mark fields as intentionally containing PII
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .PrivateAttributes(new List<string> { "email", "phone" })
    .Build();
```

### Request Signing

POST requests to the FlagKit API can be signed with HMAC-SHA256 for integrity:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .EnableRequestSigning(true)
    .Build();
```

### Bootstrap Signature Verification

Verify bootstrap data integrity using HMAC signatures:

```csharp
// Create signed bootstrap configuration
var bootstrapConfig = new BootstrapConfig
{
    Flags = new Dictionary<string, object?>
    {
        ["feature-a"] = true,
        ["feature-b"] = "value"
    },
    Signature = "hmac-sha256-signature",
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
};

// Use signed bootstrap with verification
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .BootstrapConfig(bootstrapConfig)
    .BootstrapVerification(new BootstrapVerificationConfig
    {
        Enabled = true,
        MaxAge = 86400000,  // 24 hours in milliseconds
        OnFailure = "error"  // "warn" (default), "error", or "ignore"
    })
    .Build();
```

### Cache Encryption

Enable AES-256-CBC encryption with HMAC-SHA256 authentication for cached flag data:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .EnableCacheEncryption(true)
    .Build();
```

### Evaluation Jitter (Timing Attack Protection)

Add random delays to flag evaluations to prevent cache timing attacks:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .EvaluationJitter(new EvaluationJitterConfig
    {
        Enabled = true,
        MinMs = 5,
        MaxMs = 15
    })
    .Build();
```

### Error Sanitization

Automatically redact sensitive information from error messages:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .ErrorSanitization(new ErrorSanitizationConfig
    {
        Enabled = true,
        PreserveOriginal = false  // Set true for debugging
    })
    .Build();
// Errors will have paths, IPs, API keys, and emails redacted
```

## Event Persistence

Enable crash-resilient event persistence to prevent data loss:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_...")
    .PersistEvents(true)
    .EventStoragePath("/path/to/storage")  // Optional, defaults to temp dir
    .MaxPersistedEvents(10000)             // Optional, default 10000
    .PersistenceFlushInterval(TimeSpan.FromSeconds(1))  // Optional
    .Build();
```

Events are written to disk before being queued for sending, and automatically recovered on restart.

## Key Rotation

Support seamless API key rotation:

```csharp
var options = FlagKitOptions.CreateBuilder("sdk_primary_key")
    .SecondaryApiKey("sdk_secondary_key")
    .Build();
// SDK will automatically failover to secondary key on 401 errors
```

## All Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ApiKey` | string | Required | API key for authentication |
| `SecondaryApiKey` | string? | null | Secondary key for rotation |
| `PollingInterval` | TimeSpan | 30s | Polling interval |
| `CacheTtl` | TimeSpan | 300s | Cache TTL |
| `MaxCacheSize` | int | 1000 | Maximum cache entries |
| `CacheEnabled` | bool | true | Enable local caching |
| `EnableCacheEncryption` | bool | false | Enable AES-256-CBC encryption |
| `EventsEnabled` | bool | true | Enable event tracking |
| `EventBatchSize` | int | 10 | Events per batch |
| `EventFlushInterval` | TimeSpan | 30s | Interval between flushes |
| `Timeout` | TimeSpan | 10s | Request timeout |
| `RetryAttempts` | int | 3 | Number of retry attempts |
| `CircuitBreakerThreshold` | int | 5 | Failures before circuit opens |
| `CircuitBreakerResetTimeout` | TimeSpan | 30s | Time before half-open |
| `Bootstrap` | Dictionary? | null | Initial flag values |
| `BootstrapConfig` | BootstrapConfig? | null | Signed bootstrap data |
| `BootstrapVerification` | Config | enabled | Bootstrap verification settings |
| `LocalPort` | int? | null | Local development port |
| `StrictPIIMode` | bool | false | Error on PII detection |
| `PrivateAttributes` | List<string>? | null | Fields to exclude from server |
| `EnableRequestSigning` | bool | false | Enable request signing |
| `PersistEvents` | bool | false | Enable event persistence |
| `EventStoragePath` | string? | temp dir | Event storage directory |
| `MaxPersistedEvents` | int | 10000 | Max persisted events |
| `PersistenceFlushInterval` | TimeSpan | 1s | Persistence flush interval |
| `EvaluationJitter` | Config | disabled | Timing attack protection |
| `ErrorSanitization` | Config | enabled | Sanitize error messages |

## License

MIT License - see LICENSE file for details.
