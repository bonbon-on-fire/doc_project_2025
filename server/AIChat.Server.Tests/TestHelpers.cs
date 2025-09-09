namespace AIChat.Server.Tests;

/// <summary>
/// Helper methods for test isolation and setup
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Generates a unique user ID for test isolation.
    /// Each test gets its own user ID to prevent data conflicts between tests.
    /// </summary>
    /// <param name="testName">Optional test name to include in the ID for debugging</param>
    /// <returns>A unique user ID string</returns>
    public static string GenerateUniqueUserId(string? testName = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString("N")[..8];

        if (!string.IsNullOrEmpty(testName))
        {
            // Sanitize test name to remove invalid characters
            var sanitized = testName.Replace(" ", "-").Replace("_", "-").ToLowerInvariant();
            return $"test-{sanitized}-{timestamp}-{guid}";
        }

        return $"test-user-{timestamp}-{guid}";
    }

    /// <summary>
    /// Generates multiple unique user IDs for tests that need multiple users
    /// </summary>
    /// <param name="count">Number of user IDs to generate</param>
    /// <param name="testName">Optional test name prefix</param>
    /// <returns>Array of unique user IDs</returns>
    public static string[] GenerateUniqueUserIds(int count, string? testName = null)
    {
        var userIds = new string[count];
        for (var i = 0; i < count; i++)
        {
            var name = string.IsNullOrEmpty(testName) ? $"user-{i}" : $"{testName}-{i}";
            userIds[i] = GenerateUniqueUserId(name);
        }
        return userIds;
    }
}
