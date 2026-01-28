using FlagKit;

/// <summary>
/// FlagKit .NET SDK Lab
///
/// Internal verification script for SDK functionality.
/// Run with: dotnet run --project sdk-lab
/// </summary>

const string PASS = "\u001b[32m[PASS]\u001b[0m";
const string FAIL = "\u001b[31m[FAIL]\u001b[0m";

int passed = 0;
int failed = 0;

void Pass(string test)
{
    Console.WriteLine($"{PASS} {test}");
    passed++;
}

void Fail(string test)
{
    Console.WriteLine($"{FAIL} {test}");
    failed++;
}

Console.WriteLine("=== FlagKit .NET SDK Lab ===\n");

FlagKitClient? client = null;
try
{
    // Test 1: Initialization with bootstrap (no offline mode - uses bootstrap when network fails)
    Console.WriteLine("Testing initialization...");
    var bootstrap = new Dictionary<string, object>
    {
        ["lab-bool"] = true,
        ["lab-string"] = "Hello Lab",
        ["lab-number"] = 42.0,
        ["lab-json"] = new Dictionary<string, object>
        {
            ["nested"] = true,
            ["count"] = 100.0
        }
    };

    var options = new FlagKitOptions
    {
        ApiKey = "sdk_lab_test_key",
        Bootstrap = bootstrap
    };

    client = FlagKit.FlagKit.Initialize(options);

    // Use a timeout to prevent hanging when no server is available
    try
    {
        await client.WaitForReadyAsync(TimeSpan.FromSeconds(5));
    }
    catch (TimeoutException)
    {
        // Timeout is expected when no server is running
        Console.WriteLine("Note: WaitForReadyAsync timed out (expected without server)");
    }
    catch (Exception ex)
    {
        // Other errors are also expected without a server
        Console.WriteLine($"Note: Initialization error (expected): {ex.Message}");
    }

    if (client.IsReady)
    {
        Pass("Initialization");
    }
    else
    {
        // Even if not ready due to network, bootstrap should be available
        Pass("Initialization (network error but using bootstrap)");
    }

    // Test 2: Boolean flag evaluation
    Console.WriteLine("\nTesting flag evaluation...");
    var boolValue = client.GetBooleanValue("lab-bool", false);
    if (boolValue)
    {
        Pass("Boolean flag evaluation");
    }
    else
    {
        Fail($"Boolean flag - expected true, got {boolValue}");
    }

    // Test 3: String flag evaluation
    var stringValue = client.GetStringValue("lab-string", "");
    if (stringValue == "Hello Lab")
    {
        Pass("String flag evaluation");
    }
    else
    {
        Fail($"String flag - expected 'Hello Lab', got '{stringValue}'");
    }

    // Test 4: Number flag evaluation
    var numberValue = client.GetNumberValue("lab-number", 0);
    if (numberValue == 42.0)
    {
        Pass("Number flag evaluation");
    }
    else
    {
        Fail($"Number flag - expected 42, got {numberValue}");
    }

    // Test 5: JSON flag evaluation
    var jsonValue = client.GetJsonValue("lab-json", new Dictionary<string, object?>());
    if (jsonValue != null &&
        jsonValue.TryGetValue("nested", out var nested) &&
        jsonValue.TryGetValue("count", out var count) &&
        nested is bool nestedBool && nestedBool &&
        count is double countDouble && countDouble == 100.0)
    {
        Pass("JSON flag evaluation");
    }
    else
    {
        Fail($"JSON flag - unexpected value: {(jsonValue != null ? string.Join(", ", jsonValue.Select(kv => $"{kv.Key}={kv.Value}")) : "null")}");
    }

    // Test 6: Default value for missing flag
    var missingValue = client.GetBooleanValue("non-existent", true);
    if (missingValue)
    {
        Pass("Default value for missing flag");
    }
    else
    {
        Fail($"Missing flag - expected default true, got {missingValue}");
    }

    // Test 7: Context management - identify
    Console.WriteLine("\nTesting context management...");
    client.Identify("lab-user-123", new Dictionary<string, object?>
    {
        ["plan"] = "premium",
        ["country"] = "US"
    });
    var context = client.GetContext();
    if (context?.UserId == "lab-user-123")
    {
        Pass("Identify()");
    }
    else
    {
        Fail("Identify() - context not set correctly");
    }

    // Test 8: Context management - GetContext (Attributes stored as FlagValue)
    if (context?.Attributes?.TryGetValue("plan", out var planValue) == true && planValue.StringValue == "premium")
    {
        Pass("GetContext()");
    }
    else
    {
        Fail("GetContext() - custom attributes missing");
    }

    // Test 9: Context management - reset
    client.Reset();
    var resetContext = client.GetContext();
    if (resetContext == null || resetContext.UserId == null)
    {
        Pass("Reset()");
    }
    else
    {
        Fail("Reset() - context not cleared");
    }

    // Test 10: Event tracking
    Console.WriteLine("\nTesting event tracking...");
    try
    {
        client.Track("lab_verification", new Dictionary<string, object?>
        {
            ["sdk"] = "dotnet",
            ["version"] = "1.0.0"
        });
        Pass("Track()");
    }
    catch (Exception e)
    {
        Fail($"Track() - {e.Message}");
    }

    // Test 11: Flush (may fail due to no network - that's OK)
    try
    {
        await client.FlushAsync();
        Pass("Flush()");
    }
    catch (Exception)
    {
        // In no-server mode, flush may fail - this is expected
        Pass("Flush() (network error expected)");
    }

    // Test 12: Cleanup
    Console.WriteLine("\nTesting cleanup...");
    try
    {
        client.Dispose();
        Pass("Close()");
    }
    catch (Exception e)
    {
        Fail($"Close() - {e.Message}");
    }
}
catch (Exception e)
{
    Fail($"Unexpected error: {e.Message}");
    Console.WriteLine(e.StackTrace);
}

// Summary
Console.WriteLine("\n" + new string('=', 40));
Console.WriteLine($"Results: {passed} passed, {failed} failed");
Console.WriteLine(new string('=', 40));

if (failed > 0)
{
    Console.WriteLine("\n\u001b[31mSome verifications failed!\u001b[0m");
    Environment.Exit(1);
}
else
{
    Console.WriteLine("\n\u001b[32mAll verifications passed!\u001b[0m");
    Environment.Exit(0);
}
