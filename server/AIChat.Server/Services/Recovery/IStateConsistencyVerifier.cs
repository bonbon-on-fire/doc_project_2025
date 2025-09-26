using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Verifies consistency and integrity of reconstructed state.
/// Ensures reconstructed state is valid and consistent with event streams and business rules.
/// Follows the Single Responsibility Principle by focusing solely on consistency verification.
/// </summary>
public interface IStateConsistencyVerifier
{
    /// <summary>
    /// Gets the name of the consistency verifier implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Verifies reconstructed state consistency against event stream.
    /// This is the primary verification method that validates state integrity.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="reconstructedState">The reconstructed state to verify</param>
    /// <param name="version">The version of the reconstructed state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The consistency verification result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or reconstructedState is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when verification fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ConsistencyVerificationResult> VerifyConsistencyAsync<T>(
        string grainId,
        string grainType,
        T reconstructedState,
        long version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates business rules and constraints for grain state.
    /// Ensures the state satisfies domain-specific requirements.
    /// </summary>
    /// <typeparam name="T">The type of grain state to validate</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The business rule validation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or state is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<BusinessRuleValidationResult> ValidateBusinessRulesAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies data integrity using checksums and hash validation.
    /// Ensures the state data has not been corrupted during reconstruction.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="expectedHash">Optional expected hash to verify against</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The data integrity verification result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or state is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when verification fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<DataIntegrityVerificationResult> VerifyDataIntegrityAsync<T>(
        string grainId,
        string grainType,
        T state,
        string? expectedHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that the state is aligned with the event stream sequence.
    /// Ensures events and state are in sync and no events are missing or out of order.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The current state</param>
    /// <param name="expectedVersion">The expected version based on event stream</param>
    /// <param name="projection">The projection used to rebuild the state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The event stream alignment verification result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, state, or projection is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when verification fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventStreamAlignmentResult> VerifyEventStreamAlignmentAsync<T>(
        string grainId,
        string grainType,
        T state,
        long expectedVersion,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies cross-grain consistency for related grains.
    /// Ensures that interdependent grains have consistent states.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="relatedGrains">Identifiers of related grains to check consistency with</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The cross-grain consistency verification result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or state is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when verification fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<CrossGrainConsistencyResult> VerifyCrossGrainConsistencyAsync<T>(
        string grainId,
        string grainType,
        T state,
        IReadOnlyList<string>? relatedGrains = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive consistency verification combining all validation types.
    /// Provides a complete consistency assessment of the grain state.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="version">The version of the state</param>
    /// <param name="projection">The projection used to rebuild the state</param>
    /// <param name="relatedGrains">Identifiers of related grains to check consistency with</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The comprehensive consistency verification result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, state, or projection is null</exception>
    /// <exception cref="StateConsistencyVerificationException">Thrown when verification fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ComprehensiveConsistencyResult> PerformComprehensiveVerificationAsync<T>(
        string grainId,
        string grainType,
        T state,
        long version,
        IEventProjection<T> projection,
        IReadOnlyList<string>? relatedGrains = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets consistency verification metrics and statistics.
    /// Used for monitoring and performance analysis.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Consistency verification metrics and statistics</returns>
    /// <exception cref="StateConsistencyVerificationException">Thrown when metrics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ConsistencyVerificationMetrics> GetVerificationMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of data integrity verification.
/// </summary>
public record DataIntegrityVerificationResult
{
    /// <summary>
    /// Whether the data integrity verification was successful.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The computed hash of the state data.
    /// </summary>
    public string? ComputedHash { get; init; }

    /// <summary>
    /// The expected hash, if provided.
    /// </summary>
    public string? ExpectedHash { get; init; }

    /// <summary>
    /// Whether the hash verification passed.
    /// </summary>
    public bool HashVerificationPassed { get; init; }

    /// <summary>
    /// Whether the data structure is intact.
    /// </summary>
    public bool DataStructureIntact { get; init; } = true;

    /// <summary>
    /// Data integrity issues found, if any.
    /// </summary>
    public IReadOnlyList<string> IntegrityIssues { get; init; } = [];

    /// <summary>
    /// Data integrity issues (alias for IntegrityIssues for compatibility).
    /// </summary>
    public IReadOnlyList<string> Issues => IntegrityIssues;

    /// <summary>
    /// Data integrity warnings found, if any.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Confidence level in the integrity verification (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; init; } = 1.0;

    /// <summary>
    /// Creates a valid data integrity result.
    /// </summary>
    /// <param name="computedHash">The computed hash</param>
    /// <param name="expectedHash">The expected hash, if any</param>
    /// <returns>A valid data integrity result</returns>
    public static DataIntegrityVerificationResult Valid(string? computedHash = null, string? expectedHash = null)
    {
        return new DataIntegrityVerificationResult
        {
            IsValid = true,
            ComputedHash = computedHash,
            ExpectedHash = expectedHash,
            HashVerificationPassed = true,
            DataStructureIntact = true
        };
    }

    /// <summary>
    /// Creates an invalid data integrity result.
    /// </summary>
    /// <param name="integrityIssues">The integrity issues found</param>
    /// <param name="computedHash">The computed hash</param>
    /// <param name="expectedHash">The expected hash, if any</param>
    /// <returns>An invalid data integrity result</returns>
    public static DataIntegrityVerificationResult Invalid(
        IReadOnlyList<string> integrityIssues,
        string? computedHash = null,
        string? expectedHash = null)
    {
        return new DataIntegrityVerificationResult
        {
            IsValid = false,
            IntegrityIssues = integrityIssues,
            ComputedHash = computedHash,
            ExpectedHash = expectedHash,
            HashVerificationPassed = false,
            DataStructureIntact = false
        };
    }

    /// <summary>
    /// Creates a valid data integrity result (alias for Valid method).
    /// </summary>
    /// <param name="computedHash">The computed hash</param>
    /// <param name="expectedHash">The expected hash, if any</param>
    /// <returns>A valid data integrity result</returns>
    public static DataIntegrityVerificationResult CreateValid(string? computedHash = null, string? expectedHash = null)
    {
        return Valid(computedHash, expectedHash);
    }

    /// <summary>
    /// Creates an invalid data integrity result (alias for Invalid method).
    /// </summary>
    /// <param name="integrityIssues">The integrity issues found</param>
    /// <param name="computedHash">The computed hash</param>
    /// <param name="expectedHash">The expected hash, if any</param>
    /// <returns>An invalid data integrity result</returns>
    public static DataIntegrityVerificationResult CreateInvalid(
        IReadOnlyList<string> integrityIssues,
        string? computedHash = null,
        string? expectedHash = null)
    {
        return Invalid(integrityIssues, computedHash, expectedHash);
    }
}

/// <summary>
/// Represents the result of event stream alignment verification.
/// </summary>
public record EventStreamAlignmentResult
{
    /// <summary>
    /// Whether the state is aligned with the event stream.
    /// </summary>
    public required bool IsAligned { get; init; }

    /// <summary>
    /// The current version of the state.
    /// </summary>
    public long CurrentVersion { get; init; }

    /// <summary>
    /// The expected version based on the event stream.
    /// </summary>
    public long ExpectedVersion { get; init; }

    /// <summary>
    /// The version difference (expected - current).
    /// </summary>
    public long VersionDifference => ExpectedVersion - CurrentVersion;

    /// <summary>
    /// Alignment issues found, if any.
    /// </summary>
    public IReadOnlyList<string> AlignmentIssues { get; init; } = [];

    /// <summary>
    /// Missing events that should be applied to reach alignment.
    /// </summary>
    public IReadOnlyList<string> MissingEvents { get; init; } = [];

    /// <summary>
    /// Extra events that shouldn't be in the current state.
    /// </summary>
    public IReadOnlyList<string> ExtraEvents { get; init; } = [];

    /// <summary>
    /// Alignment issues (alias for AlignmentIssues for compatibility).
    /// </summary>
    public IReadOnlyList<string> Issues => AlignmentIssues;

    /// <summary>
    /// Alignment warnings found, if any.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Creates an aligned result.
    /// </summary>
    /// <param name="currentVersion">The current version</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <returns>An aligned result</returns>
    public static EventStreamAlignmentResult Aligned(long currentVersion, long expectedVersion)
    {
        return new EventStreamAlignmentResult
        {
            IsAligned = true,
            CurrentVersion = currentVersion,
            ExpectedVersion = expectedVersion
        };
    }

    /// <summary>
    /// Creates a misaligned result.
    /// </summary>
    /// <param name="currentVersion">The current version</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="alignmentIssues">The alignment issues found</param>
    /// <param name="missingEvents">Missing events</param>
    /// <param name="extraEvents">Extra events</param>
    /// <returns>A misaligned result</returns>
    public static EventStreamAlignmentResult Misaligned(
        long currentVersion,
        long expectedVersion,
        IReadOnlyList<string> alignmentIssues,
        IReadOnlyList<string>? missingEvents = null,
        IReadOnlyList<string>? extraEvents = null)
    {
        return new EventStreamAlignmentResult
        {
            IsAligned = false,
            CurrentVersion = currentVersion,
            ExpectedVersion = expectedVersion,
            AlignmentIssues = alignmentIssues,
            MissingEvents = missingEvents ?? [],
            ExtraEvents = extraEvents ?? []
        };
    }

    /// <summary>
    /// Creates an aligned result (alias for Aligned method).
    /// </summary>
    /// <param name="currentVersion">The current version</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <returns>An aligned result</returns>
    public static EventStreamAlignmentResult CreateAligned(long currentVersion, long expectedVersion)
    {
        return Aligned(currentVersion, expectedVersion);
    }

    /// <summary>
    /// Creates a misaligned result (alias for Misaligned method).
    /// </summary>
    /// <param name="currentVersion">The current version</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="alignmentIssues">The alignment issues found</param>
    /// <param name="missingEvents">Missing events</param>
    /// <param name="extraEvents">Extra events</param>
    /// <returns>A misaligned result</returns>
    public static EventStreamAlignmentResult CreateMisaligned(
        long currentVersion,
        long expectedVersion,
        IReadOnlyList<string> alignmentIssues,
        IReadOnlyList<string>? missingEvents = null,
        IReadOnlyList<string>? extraEvents = null)
    {
        return Misaligned(currentVersion, expectedVersion, alignmentIssues, missingEvents, extraEvents);
    }
}

/// <summary>
/// Represents the result of cross-grain consistency verification.
/// </summary>
public record CrossGrainConsistencyResult
{
    /// <summary>
    /// Whether cross-grain consistency is maintained.
    /// </summary>
    public required bool IsConsistent { get; init; }

    /// <summary>
    /// The grains that were checked for consistency.
    /// </summary>
    public IReadOnlyList<string> CheckedGrains { get; init; } = [];

    /// <summary>
    /// Consistency issues found between grains.
    /// </summary>
    public IReadOnlyList<string> ConsistencyIssues { get; init; } = [];

    /// <summary>
    /// Grains that have inconsistent states.
    /// </summary>
    public IReadOnlyList<string> InconsistentGrains { get; init; } = [];

    /// <summary>
    /// Consistency constraints that were violated.
    /// </summary>
    public IReadOnlyList<string> ViolatedConstraints { get; init; } = [];

    /// <summary>
    /// Creates a consistent result.
    /// </summary>
    /// <param name="checkedGrains">The grains that were checked</param>
    /// <returns>A consistent result</returns>
    public static CrossGrainConsistencyResult Consistent(IReadOnlyList<string>? checkedGrains = null)
    {
        return new CrossGrainConsistencyResult
        {
            IsConsistent = true,
            CheckedGrains = checkedGrains ?? []
        };
    }

    /// <summary>
    /// Creates an inconsistent result.
    /// </summary>
    /// <param name="consistencyIssues">The consistency issues found</param>
    /// <param name="inconsistentGrains">The grains with inconsistent states</param>
    /// <param name="violatedConstraints">The constraints that were violated</param>
    /// <returns>An inconsistent result</returns>
    public static CrossGrainConsistencyResult Inconsistent(
        IReadOnlyList<string> consistencyIssues,
        IReadOnlyList<string>? inconsistentGrains = null,
        IReadOnlyList<string>? violatedConstraints = null)
    {
        return new CrossGrainConsistencyResult
        {
            IsConsistent = false,
            ConsistencyIssues = consistencyIssues,
            InconsistentGrains = inconsistentGrains ?? [],
            ViolatedConstraints = violatedConstraints ?? []
        };
    }
}

/// <summary>
/// Represents the result of comprehensive consistency verification.
/// </summary>
public record ComprehensiveConsistencyResult
{
    /// <summary>
    /// Whether the overall consistency verification passed.
    /// </summary>
    public required bool IsOverallConsistent { get; init; }

    /// <summary>
    /// The basic consistency verification result.
    /// </summary>
    public ConsistencyVerificationResult? BasicConsistency { get; init; }

    /// <summary>
    /// The business rule validation result.
    /// </summary>
    public BusinessRuleValidationResult? BusinessRuleValidation { get; init; }

    /// <summary>
    /// The data integrity verification result.
    /// </summary>
    public DataIntegrityVerificationResult? DataIntegrityValidation { get; init; }

    /// <summary>
    /// The event stream alignment result.
    /// </summary>
    public EventStreamAlignmentResult? EventStreamAlignment { get; init; }

    /// <summary>
    /// The cross-grain consistency result.
    /// </summary>
    public CrossGrainConsistencyResult? CrossGrainConsistency { get; init; }

    /// <summary>
    /// Overall consistency score (0.0 to 100.0).
    /// </summary>
    public double ConsistencyScore { get; init; }

    /// <summary>
    /// Summary of all issues found across all verification types.
    /// </summary>
    public IReadOnlyList<string> AllIssues { get; init; } = [];

    /// <summary>
    /// Recommendations for addressing consistency issues.
    /// </summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>
    /// Time taken for the comprehensive verification.
    /// </summary>
    public TimeSpan VerificationTime { get; init; }

    /// <summary>
    /// Creates a result indicating overall consistency.
    /// </summary>
    /// <param name="consistencyScore">The overall consistency score</param>
    /// <param name="verificationTime">Time taken for verification</param>
    /// <returns>A consistent comprehensive result</returns>
    public static ComprehensiveConsistencyResult OverallConsistent(
        double consistencyScore,
        TimeSpan verificationTime)
    {
        return new ComprehensiveConsistencyResult
        {
            IsOverallConsistent = true,
            ConsistencyScore = consistencyScore,
            VerificationTime = verificationTime
        };
    }

    /// <summary>
    /// Creates a result indicating overall inconsistency.
    /// </summary>
    /// <param name="allIssues">All issues found</param>
    /// <param name="recommendations">Recommendations for fixes</param>
    /// <param name="consistencyScore">The overall consistency score</param>
    /// <param name="verificationTime">Time taken for verification</param>
    /// <returns>An inconsistent comprehensive result</returns>
    public static ComprehensiveConsistencyResult OverallInconsistent(
        IReadOnlyList<string> allIssues,
        IReadOnlyList<string> recommendations,
        double consistencyScore,
        TimeSpan verificationTime)
    {
        return new ComprehensiveConsistencyResult
        {
            IsOverallConsistent = false,
            AllIssues = allIssues,
            Recommendations = recommendations,
            ConsistencyScore = consistencyScore,
            VerificationTime = verificationTime
        };
    }
}

/// <summary>
/// Represents metrics and statistics for consistency verification operations.
/// </summary>
public record ConsistencyVerificationMetrics
{
    /// <summary>
    /// Total number of consistency verification operations performed.
    /// </summary>
    public long TotalVerificationOperations { get; init; }

    /// <summary>
    /// Number of verifications that passed.
    /// </summary>
    public long PassedVerifications { get; init; }

    /// <summary>
    /// Number of verifications that failed.
    /// </summary>
    public long FailedVerifications { get; init; }

    /// <summary>
    /// Average time taken for verification operations in milliseconds.
    /// </summary>
    public double AverageVerificationTimeMs { get; init; }

    /// <summary>
    /// Verification success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalVerificationOperations > 0
        ? (double)PassedVerifications / TotalVerificationOperations * 100
        : 0;

    /// <summary>
    /// Distribution of verification types performed.
    /// </summary>
    public Dictionary<string, long> VerificationTypeDistribution { get; init; } = [];

    /// <summary>
    /// Distribution of consistency issues found.
    /// </summary>
    public Dictionary<string, long> ConsistencyIssueDistribution { get; init; } = [];

    /// <summary>
    /// Average consistency score across all verifications.
    /// </summary>
    public double AverageConsistencyScore { get; init; }

    /// <summary>
    /// Timestamp when metrics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty verification metrics.
    /// </summary>
    /// <returns>Empty verification metrics</returns>
    public static ConsistencyVerificationMetrics Empty()
    {
        return new ConsistencyVerificationMetrics
        {
            TotalVerificationOperations = 0,
            PassedVerifications = 0,
            FailedVerifications = 0,
            AverageVerificationTimeMs = 0.0,
            AverageConsistencyScore = 0.0
        };
    }
}