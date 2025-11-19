using AIChat.Server.Services.EventStore;
using AIChat.Server.Services.EventStore.Extensions;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Unit tests for snapshot policy evaluation functionality.
/// Tests the core logic for determining when snapshots should be created or cleaned up.
/// </summary>
public class SnapshotPolicyEvaluationTests
{
    [Fact]
    public void SnapshotCreationPolicy_ByEventCount_ShouldCreateCorrectPolicy()
    {
        // Arrange
        const int eventCount = 100;

        // Act
        var policy = SnapshotCreationPolicy.ByEventCount(eventCount);

        // Assert
        _ = policy.EventCountThreshold.Should().Be(eventCount);
        _ = policy.TimeThreshold.Should().BeNull();
        _ = policy.VersionThreshold.Should().BeNull();
        _ = policy.CreateOnDeactivation.Should().BeFalse();
    }

    [Fact]
    public void SnapshotCreationPolicy_ByTime_ShouldCreateCorrectPolicy()
    {
        // Arrange
        var timeSpan = TimeSpan.FromHours(2);

        // Act
        var policy = SnapshotCreationPolicy.ByTime(timeSpan);

        // Assert
        _ = policy.TimeThreshold.Should().Be(timeSpan);
        _ = policy.EventCountThreshold.Should().BeNull();
        _ = policy.VersionThreshold.Should().BeNull();
        _ = policy.CreateOnDeactivation.Should().BeFalse();
    }

    [Fact]
    public void SnapshotCreationPolicy_ByVersion_ShouldCreateCorrectPolicy()
    {
        // Arrange
        const long versionDelta = 50;

        // Act
        var policy = SnapshotCreationPolicy.ByVersion(versionDelta);

        // Assert
        _ = policy.VersionThreshold.Should().Be(versionDelta);
        _ = policy.EventCountThreshold.Should().BeNull();
        _ = policy.TimeThreshold.Should().BeNull();
        _ = policy.CreateOnDeactivation.Should().BeFalse();
    }

    [Fact]
    public void SnapshotCreationPolicy_Combined_ShouldCreateCorrectPolicy()
    {
        // Arrange
        const int eventCount = 100;
        var timeThreshold = TimeSpan.FromHours(1);
        const long versionThreshold = 25;

        // Act
        var policy = SnapshotCreationPolicy.Combined(eventCount, timeThreshold, versionThreshold);

        // Assert
        _ = policy.EventCountThreshold.Should().Be(eventCount);
        _ = policy.TimeThreshold.Should().Be(timeThreshold);
        _ = policy.VersionThreshold.Should().Be(versionThreshold);
        _ = policy.CreateOnDeactivation.Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 50, 99, false)] // Below threshold
    [InlineData(100, 50, 100, false)] // Below threshold (50 events since last snapshot < 100 threshold)
    [InlineData(100, 50, 150, true)]  // Above threshold
    [InlineData(100, 0, 50, false)]   // Below threshold with no existing snapshot
    [InlineData(100, -1, 100, true)]  // At threshold with no existing snapshot
    public void EventCountThreshold_ShouldEvaluateCorrectly(
        int threshold,
        long lastSnapshotVersion,
        long currentVersion,
        bool expectedResult)
    {
        // Arrange
        var policy = SnapshotCreationPolicy.ByEventCount(threshold);

        // Act
        var eventsSinceSnapshot = currentVersion - lastSnapshotVersion;
        var shouldCreate = eventsSinceSnapshot >= threshold;

        // Assert
        _ = shouldCreate.Should().Be(expectedResult);
    }

    [Fact]
    public void PolicyEvaluationResult_CreatePositive_ShouldCreateCorrectResult()
    {
        // Arrange
        const string reason = "Event count threshold exceeded";
        var triggeredCriteria = new[] { "EventCount", "TimeThreshold" };
        const double confidence = 0.95;
        const int priority = 5;

        // Act
        var result = PolicyEvaluationResult.CreatePositive(reason, triggeredCriteria, confidence, priority);

        // Assert
        _ = result.ShouldCreateSnapshot.Should().BeTrue();
        _ = result.Reason.Should().Be(reason);
        _ = result.TriggeredCriteria.Should().BeEquivalentTo(triggeredCriteria);
        _ = result.Confidence.Should().Be(confidence);
        _ = result.Priority.Should().Be(priority);
    }

    [Fact]
    public void PolicyEvaluationResult_CreateNegative_ShouldCreateCorrectResult()
    {
        // Arrange
        const string reason = "No thresholds exceeded";
        const double confidence = 0.8;

        // Act
        var result = PolicyEvaluationResult.CreateNegative(reason, confidence);

        // Assert
        _ = result.ShouldCreateSnapshot.Should().BeFalse();
        _ = result.Reason.Should().Be(reason);
        _ = result.Confidence.Should().Be(confidence);
        _ = result.TriggeredCriteria.Should().BeEmpty();
        _ = result.Priority.Should().Be(0);
    }

    [Fact]
    public void RetentionEvaluationResult_CreatePositive_ShouldCreateCorrectResult()
    {
        // Arrange
        var candidates = new[]
        {
            new SnapshotCleanupCandidate
            {
                Metadata = CreateTestSnapshotMetadata("snap1"),
                CleanupReason = "Expired",
                EstimatedSpaceReclaimed = 1000
            }
        };
        const string reason = "Age-based cleanup";
        const long estimatedSpace = 1000;

        // Act
        var result = RetentionEvaluationResult.CreatePositive(candidates, reason, estimatedSpace);

        // Assert
        _ = result.ShouldCleanup.Should().BeTrue();
        _ = result.CandidatesForDeletion.Should().HaveCount(1);
        _ = result.Reason.Should().Be(reason);
        _ = result.EstimatedSpaceReclaimed.Should().Be(estimatedSpace);
    }

    [Fact]
    public void RetentionEvaluationResult_CreateNegative_ShouldCreateCorrectResult()
    {
        // Arrange
        const string reason = "No cleanup needed";

        // Act
        var result = RetentionEvaluationResult.CreateNegative(reason);

        // Assert
        _ = result.ShouldCleanup.Should().BeFalse();
        _ = result.CandidatesForDeletion.Should().BeEmpty();
        _ = result.Reason.Should().Be(reason);
        _ = result.EstimatedSpaceReclaimed.Should().Be(0);
    }

    [Fact]
    public void SnapshotPolicyContext_ShouldCalculateEventsSinceSnapshotCorrectly()
    {
        // Arrange
        const string streamId = "test-stream";
        const long currentVersion = 100;
        const long latestSnapshotVersion = 75;
        var policy = SnapshotCreationPolicy.ByEventCount(20);

        // Act
        var context = new SnapshotPolicyContext
        {
            StreamId = streamId,
            CurrentVersion = currentVersion,
            Policy = policy,
            LatestSnapshotVersion = latestSnapshotVersion
        };

        // Assert
        var eventsSinceSnapshot = context.CurrentVersion - context.LatestSnapshotVersion;
        _ = eventsSinceSnapshot.Should().Be(25);
    }

    [Fact]
    public void PolicyValidationResult_CreateValid_ShouldCreateCorrectResult()
    {
        // Arrange
        var warnings = new[] { "Performance may be impacted" };

        // Act
        var result = PolicyValidationResult.CreateValid(warnings);

        // Assert
        _ = result.IsValid.Should().BeTrue();
        _ = result.Issues.Should().BeEmpty();
        _ = result.Warnings.Should().BeEquivalentTo(warnings);
    }

    [Fact]
    public void PolicyValidationResult_CreateInvalid_ShouldCreateCorrectResult()
    {
        // Arrange
        var issues = new[] { "Threshold cannot be negative" };
        var warnings = new[] { "Consider using higher threshold" };

        // Act
        var result = PolicyValidationResult.CreateInvalid(issues, warnings);

        // Assert
        _ = result.IsValid.Should().BeFalse();
        _ = result.Issues.Should().BeEquivalentTo(issues);
        _ = result.Warnings.Should().BeEquivalentTo(warnings);
    }

    [Fact]
    public void StreamStatistics_AverageEventSize_ShouldCalculateCorrectly()
    {
        // Arrange
        const long eventCount = 100;
        const long estimatedSize = 50000;

        // Act
        var statistics = new StreamStatistics
        {
            StreamId = "test-stream",
            EventCount = eventCount,
            EstimatedSize = estimatedSize,
            LastActivity = DateTimeOffset.UtcNow
        };

        // Assert
        _ = statistics.AverageEventSize.Should().Be(500.0);
    }

    [Fact]
    public void StreamStatistics_AverageEventSize_WithZeroEvents_ShouldReturnZero()
    {
        // Arrange & Act
        var statistics = new StreamStatistics
        {
            StreamId = "test-stream",
            EventCount = 0,
            EstimatedSize = 1000,
            LastActivity = DateTimeOffset.UtcNow
        };

        // Assert
        _ = statistics.AverageEventSize.Should().Be(0.0);
    }

    [Theory]
    [InlineData(10, 5, 2.0)]
    [InlineData(1000, 800, 1.25)]
    [InlineData(0, 0, 1.0)]
    public void ProcessedContent_CompressionRatio_ShouldCalculateCorrectly(
        long originalSize,
        long processedSize,
        double expectedRatio)
    {
        // Arrange & Act
        var content = new ProcessedContent
        {
            Data = [],
            ContentType = "test",
            OriginalSize = originalSize,
            ProcessedSize = processedSize
        };

        // Assert
        _ = content.CompressionRatio.Should().BeApproximately(expectedRatio, 0.001);
    }

    [Fact]
    public void ContentValidationResult_CreateValid_ShouldCreateCorrectResult()
    {
        // Arrange
        const string contentHash = "abc123";
        var metadata = new Dictionary<string, object> { ["test"] = "value" };

        // Act
        var result = ContentValidationResult.CreateValid(contentHash, metadata);

        // Assert
        _ = result.IsValid.Should().BeTrue();
        _ = result.ContentHash.Should().Be(contentHash);
        _ = result.Metadata.Should().Contain("test", "value");
        _ = result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void ContentValidationResult_CreateInvalid_ShouldCreateCorrectResult()
    {
        // Arrange
        var issues = new[] { "Checksum mismatch", "Corrupted data" };
        var metadata = new Dictionary<string, object> { ["error"] = "details" };

        // Act
        var result = ContentValidationResult.CreateInvalid(issues, metadata);

        // Assert
        _ = result.IsValid.Should().BeFalse();
        _ = result.Issues.Should().BeEquivalentTo(issues);
        _ = result.Metadata.Should().Contain("error", "details");
        _ = result.ContentHash.Should().BeNull();
    }

    [Fact]
    public void SnapshotCleanupCandidate_ShouldInitializeCorrectly()
    {
        // Arrange
        var metadata = CreateTestSnapshotMetadata("test-snapshot");
        const string cleanupReason = "Age-based cleanup";
        const int priority = 5;
        const long spaceReclaimed = 2000;

        // Act
        var candidate = new SnapshotCleanupCandidate
        {
            Metadata = metadata,
            CleanupReason = cleanupReason,
            Priority = priority,
            EstimatedSpaceReclaimed = spaceReclaimed
        };

        // Assert
        _ = candidate.Metadata.Should().Be(metadata);
        _ = candidate.CleanupReason.Should().Be(cleanupReason);
        _ = candidate.Priority.Should().Be(priority);
        _ = candidate.EstimatedSpaceReclaimed.Should().Be(spaceReclaimed);
    }

    /// <summary>
    /// Creates a test snapshot metadata object for testing purposes.
    /// </summary>
    private static SnapshotMetadata CreateTestSnapshotMetadata(string id)
    {
        return new SnapshotMetadata
        {
            Id = id,
            StreamId = "test-stream",
            Version = 1,
            ContentHash = "hash123",
            Timestamp = DateTimeOffset.UtcNow,
            CompressedSize = 1000,
            UncompressedSize = 2000,
            CompressionType = "gzip",
            StateType = "TestState"
        };
    }
}

/// <summary>
/// Unit tests for snapshot content processing extensions.
/// Tests the extension interfaces for content transformation and validation.
/// </summary>
public class SnapshotContentProcessingTests
{
    [Fact]
    public void ProcessedContent_CompressionRatio_WithZeroOriginalSize_ShouldReturnOne()
    {
        // Arrange & Act
        var content = new ProcessedContent
        {
            Data = [],
            ContentType = "test",
            OriginalSize = 0,
            ProcessedSize = 100
        };

        // Assert
        _ = content.CompressionRatio.Should().Be(1.0);
    }

    [Fact]
    public void ProcessedContent_WithMetadata_ShouldStoreCorrectly()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var metadata = new Dictionary<string, object>
        {
            ["algorithm"] = "gzip",
            ["level"] = 6
        };

        // Act
        var content = new ProcessedContent
        {
            Data = data,
            ContentType = "application/octet-stream",
            OriginalSize = 100,
            ProcessedSize = 80,
            Metadata = metadata,
            ProcessingTime = TimeSpan.FromMilliseconds(50)
        };

        // Assert
        _ = content.Data.Should().BeEquivalentTo(data);
        _ = content.ContentType.Should().Be("application/octet-stream");
        _ = content.Metadata.Should().Contain("algorithm", "gzip");
        _ = content.Metadata.Should().Contain("level", 6);
        _ = content.ProcessingTime.Should().Be(TimeSpan.FromMilliseconds(50));
    }
}

/// <summary>
/// Unit tests for snapshot restore and creation result objects.
/// Tests the result types used in snapshot operations.
/// </summary>
public class SnapshotResultTests
{
    [Fact]
    public void SnapshotRestoreResult_CreateSuccess_ShouldCreateCorrectResult()
    {
        // Arrange
        var state = new { Name = "Test", Value = 123 };
        const long finalVersion = 100;
        const long snapshotVersion = 75;
        const int eventsReplayed = 25;
        var restorationTime = TimeSpan.FromMilliseconds(500);
        var timeSaved = TimeSpan.FromSeconds(2);

        // Act
        var result = SnapshotRestoreResult.CreateSuccess(
            state,
            finalVersion,
            snapshotVersion,
            eventsReplayed,
            restorationTime,
            timeSaved);

        // Assert
        _ = result.Success.Should().BeTrue();
        _ = result.State.Should().Be(state);
        _ = result.FinalVersion.Should().Be(finalVersion);
        _ = result.SnapshotVersion.Should().Be(snapshotVersion);
        _ = result.EventsReplayed.Should().Be(eventsReplayed);
        _ = result.RestorationTime.Should().Be(restorationTime);
        _ = result.TimeSaved.Should().Be(timeSaved);
        _ = result.Error.Should().BeNull();
    }

    [Fact]
    public void SnapshotRestoreResult_CreateFailure_ShouldCreateCorrectResult()
    {
        // Arrange
        const string error = "Snapshot corruption detected";
        const SnapshotErrorCode errorCode = SnapshotErrorCode.IntegrityError;
        var restorationTime = TimeSpan.FromMilliseconds(100);

        // Act
        var result = SnapshotRestoreResult.CreateFailure<object>(
            error,
            errorCode,
            restorationTime: restorationTime);

        // Assert
        _ = result.Success.Should().BeFalse();
        _ = result.Error.Should().Be(error);
        _ = result.ErrorCode.Should().Be(errorCode);
        _ = result.RestorationTime.Should().Be(restorationTime);
        _ = result.State.Should().BeNull();
    }

    [Fact]
    public void SnapshotValidationResult_CreateValid_ShouldHaveAllValidFlags()
    {
        // Arrange
        var metadata = CreateTestSnapshotMetadata("test-snapshot");

        // Act
        var result = SnapshotValidationResult.CreateValid(metadata);

        // Assert
        _ = result.IsValid.Should().BeTrue();
        _ = result.ContentHashValid.Should().BeTrue();
        _ = result.CompressionValid.Should().BeTrue();
        _ = result.SerializationValid.Should().BeTrue();
        _ = result.Issues.Should().BeEmpty();
        _ = result.Metadata.Should().Be(metadata);
    }

    [Fact]
    public void SnapshotValidationResult_CreateInvalid_ShouldHaveCorrectFlags()
    {
        // Arrange
        var issues = new[] { "Content hash mismatch", "Unable to decompress" };

        // Act
        var result = SnapshotValidationResult.CreateInvalid(
            issues,
            contentHashValid: false,
            compressionValid: false,
            serializationValid: true);

        // Assert
        _ = result.IsValid.Should().BeFalse();
        _ = result.ContentHashValid.Should().BeFalse();
        _ = result.CompressionValid.Should().BeFalse();
        _ = result.SerializationValid.Should().BeTrue();
        _ = result.Issues.Should().BeEquivalentTo(issues);
    }

    [Fact]
    public void SnapshotCleanupResult_CreateSuccess_ShouldCreateCorrectResult()
    {
        // Arrange
        const int snapshotsDeleted = 5;
        const long spaceReclaimed = 10000;
        var cleanupDetails = new[]
        {
            new SnapshotCleanupDetail
            {
                SnapshotId = "snap1",
                StreamId = "stream1",
                Version = 1,
                Reason = "Expired",
                Size = 2000,
                Age = TimeSpan.FromDays(31)
            }
        };
        var cleanupTime = TimeSpan.FromSeconds(2);

        // Act
        var result = SnapshotCleanupResult.CreateSuccess(
            snapshotsDeleted,
            spaceReclaimed,
            cleanupDetails,
            cleanupTime);

        // Assert
        _ = result.Success.Should().BeTrue();
        _ = result.SnapshotsDeleted.Should().Be(snapshotsDeleted);
        _ = result.SpaceReclaimed.Should().Be(spaceReclaimed);
        _ = result.CleanupDetails.Should().HaveCount(1);
        _ = result.CleanupTime.Should().Be(cleanupTime);
        _ = result.Error.Should().BeNull();
    }

    /// <summary>
    /// Creates a test snapshot metadata object for testing purposes.
    /// </summary>
    private static SnapshotMetadata CreateTestSnapshotMetadata(string id)
    {
        return new SnapshotMetadata
        {
            Id = id,
            StreamId = "test-stream",
            Version = 1,
            ContentHash = "hash123",
            Timestamp = DateTimeOffset.UtcNow,
            CompressedSize = 1000,
            UncompressedSize = 2000,
            CompressionType = "gzip",
            StateType = "TestState"
        };
    }
}
