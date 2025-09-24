using AIChat.Server.Services.EventStore;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Verifies consistency and integrity of grain state data.
/// Implements basic verification focused on structural integrity and event stream alignment.
/// </summary>
public sealed class StateConsistencyVerifier : IStateConsistencyVerifier
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<StateConsistencyVerifier> _logger;

    /// <summary>
    /// Gets the name of this consistency verifier implementation.
    /// </summary>
    public string Name => "DefaultStateConsistencyVerifier";

    /// <summary>
    /// Initializes a new instance of the StateConsistencyVerifier class.
    /// </summary>
    /// <param name="eventStore">The event store for accessing events</param>
    /// <param name="logger">The logger for diagnostic output</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
    public StateConsistencyVerifier(
        IEventStore eventStore,
        ILogger<StateConsistencyVerifier> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs comprehensive consistency verification of grain state.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive verification result with detailed findings</returns>
    public async Task<ConsistencyVerificationResult> VerifyConsistencyAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        return await VerifyConsistencyInternalAsync(grainId, grainType, state, null, cancellationToken);
    }

    /// <summary>
    /// Performs comprehensive consistency verification of grain state with version check.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="reconstructedState">The state to verify</param>
    /// <param name="version">The version to verify against</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive verification result with detailed findings</returns>
    public async Task<ConsistencyVerificationResult> VerifyConsistencyAsync<T>(
        string grainId,
        string grainType,
        T reconstructedState,
        long version,
        CancellationToken cancellationToken = default)
    {
        return await VerifyConsistencyInternalAsync(grainId, grainType, reconstructedState, version, cancellationToken);
    }

    /// <summary>
    /// Internal method for comprehensive consistency verification.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="expectedVersion">The expected version, if provided</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive verification result with detailed findings</returns>
    private async Task<ConsistencyVerificationResult> VerifyConsistencyInternalAsync<T>(
        string grainId,
        string grainType,
        T state,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Starting consistency verification for grain {GrainId}", grainId);

            var issues = new List<string>();
            var warnings = new List<string>();

            // Verify data integrity
            var dataIntegrityResult = await VerifyDataIntegrityAsync(grainId, grainType, state, cancellationToken);
            if (!dataIntegrityResult.IsValid)
            {
                issues.AddRange(dataIntegrityResult.Issues);
            }
            if (dataIntegrityResult.Warnings.Any())
            {
                warnings.AddRange(dataIntegrityResult.Warnings);
            }

            // Verify business rules
            var businessRulesResult = await ValidateBusinessRulesAsync(grainId, grainType, state, cancellationToken);
            if (!businessRulesResult.IsValid)
            {
                issues.AddRange(businessRulesResult.Violations);
            }

            // Verify event stream alignment
            var alignmentResult = await VerifyEventStreamAlignmentAsync(grainId, grainType, state, cancellationToken);
            if (!alignmentResult.IsAligned)
            {
                issues.AddRange(alignmentResult.Issues);
            }
            if (alignmentResult.Warnings.Any())
            {
                warnings.AddRange(alignmentResult.Warnings);
            }

            var isConsistent = issues.Count == 0;
            var confidenceScore = CalculateConfidenceScore(dataIntegrityResult, businessRulesResult, alignmentResult);

            var result = new ConsistencyVerificationResult
            {
                IsConsistent = isConsistent,
                Issues = issues,
                Warnings = warnings,
                ConfidenceScore = confidenceScore,
                DataIntegrityResult = dataIntegrityResult,
                BusinessRulesResult = businessRulesResult,
                EventStreamAlignmentResult = alignmentResult,
                VerificationTime = stopwatch.Elapsed,
                Timestamp = DateTimeOffset.UtcNow
            };

            _logger.LogDebug(
                "Consistency verification completed for grain {GrainId} in {ElapsedMs}ms. IsConsistent: {IsConsistent}",
                grainId, stopwatch.ElapsedMilliseconds, isConsistent);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error verifying consistency for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to verify consistency for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Validates business rules and domain constraints of the state.
    /// </summary>
    /// <typeparam name="T">The type of grain state to validate</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Business rule validation result</returns>
    public Task<BusinessRuleValidationResult> ValidateBusinessRulesAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var violations = new List<string>();
            var warnings = new List<string>();

            if (state == null)
            {
                violations.Add("State cannot be null");
                return Task.FromResult(BusinessRuleValidationResult.Invalid(violations, ValidationSeverity.Critical));
            }

            // Basic structural validation
            var stateType = typeof(T);

            // Check for required properties using reflection
            var properties = stateType.GetProperties();
            foreach (var property in properties)
            {
                // Check for null required reference properties
                if (!property.PropertyType.IsValueType &&
                    property.PropertyType != typeof(string) &&
                    property.GetValue(state) == null)
                {
                    warnings.Add($"Property '{property.Name}' is null but may be required");
                }

                // Check for negative IDs if property name suggests it's an ID
                if (property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                    property.PropertyType == typeof(int))
                {
                    var value = (int?)property.GetValue(state);
                    if (value < 0)
                    {
                        violations.Add($"ID property '{property.Name}' cannot be negative: {value}");
                    }
                }

                // Check for version consistency
                if (property.Name.Equals("Version", StringComparison.OrdinalIgnoreCase) &&
                    property.PropertyType == typeof(long))
                {
                    var version = (long?)property.GetValue(state);
                    if (version < 0)
                    {
                        violations.Add($"Version cannot be negative: {version}");
                    }
                }
            }

            // Additional grain-type specific validations can be added here
            ValidateGrainSpecificRules(grainType, state, violations, warnings);

            var isValid = violations.Count == 0;

            return Task.FromResult(isValid
                ? BusinessRuleValidationResult.Valid()
                : BusinessRuleValidationResult.Invalid(violations, ValidationSeverity.Error));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error validating business rules for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to validate business rules for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Verifies data integrity using checksums and validation.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Data integrity verification result</returns>
    public Task<DataIntegrityVerificationResult> VerifyDataIntegrityAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        return VerifyDataIntegrityAsync(grainId, grainType, state, null, cancellationToken);
    }

    /// <summary>
    /// Verifies data integrity using checksums and validation with expected hash.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="expectedHash">Expected hash value for integrity verification</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Data integrity verification result</returns>
    public Task<DataIntegrityVerificationResult> VerifyDataIntegrityAsync<T>(
        string grainId,
        string grainType,
        T state,
        string? expectedHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var issues = new List<string>();
            var warnings = new List<string>();

            if (state == null)
            {
                issues.Add("State is null");
                return Task.FromResult(DataIntegrityVerificationResult.CreateInvalid(issues));
            }

            // Test JSON serialization/deserialization
            string json;
            try
            {
                json = System.Text.Json.JsonSerializer.Serialize(state);
                var deserializedState = System.Text.Json.JsonSerializer.Deserialize<T>(json);

                if (deserializedState == null)
                {
                    issues.Add("State deserialization resulted in null object");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"JSON serialization/deserialization failed: {ex.Message}");
                json = "";
            }

            // Verify expected hash if provided
            if (!string.IsNullOrEmpty(expectedHash) && !string.IsNullOrEmpty(json))
            {
                try
                {
                    var actualHash = ComputeSimpleHash(json);
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add($"Data integrity hash mismatch. Expected: {expectedHash}, Actual: {actualHash}");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Hash verification failed: {ex.Message}");
                }
            }

            // Check for circular references
            if (HasCircularReferences(state))
            {
                warnings.Add("Potential circular references detected in state object");
            }

            // Basic type consistency check
            var stateType = typeof(T);
            if (state.GetType() != stateType && !stateType.IsAssignableFrom(state.GetType()))
            {
                issues.Add($"State object type {state.GetType().Name} is not compatible with expected type {stateType.Name}");
            }

            var isValid = issues.Count == 0;

            return Task.FromResult(isValid
                ? DataIntegrityVerificationResult.CreateValid()
                : DataIntegrityVerificationResult.CreateInvalid(issues));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error verifying data integrity for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to verify data integrity for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Verifies that state is aligned with its event stream.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Event stream alignment verification result</returns>
    public async Task<EventStreamAlignmentResult> VerifyEventStreamAlignmentAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        return await VerifyEventStreamAlignmentInternalAsync(grainId, grainType, state, null, null, cancellationToken);
    }

    /// <summary>
    /// Verifies that state is aligned with its event stream with expected version.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="expectedVersion">The expected version for alignment check</param>
    /// <param name="projection">The projection for state reconstruction verification</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Event stream alignment verification result</returns>
    public async Task<EventStreamAlignmentResult> VerifyEventStreamAlignmentAsync<T>(
        string grainId,
        string grainType,
        T state,
        long expectedVersion,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return await VerifyEventStreamAlignmentInternalAsync(grainId, grainType, state, expectedVersion, projection, cancellationToken);
    }

    /// <summary>
    /// Internal method for event stream alignment verification.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="expectedVersion">Expected version, if provided</param>
    /// <param name="projection">Projection, if provided</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Event stream alignment verification result</returns>
    private async Task<EventStreamAlignmentResult> VerifyEventStreamAlignmentInternalAsync<T>(
        string grainId,
        string grainType,
        T state,
        long? expectedVersion,
        IEventProjection<T>? projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var issues = new List<string>();
            var warnings = new List<string>();
            var streamId = $"{grainType}-{grainId}";

            // Check if event stream exists
            if (!await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                if (state != null)
                {
                    warnings.Add("State exists but no corresponding event stream found");
                }

                return EventStreamAlignmentResult.CreateAligned(
                    currentVersion: GetStateVersion(state),
                    expectedVersion: 0);
            }

            var streamVersion = await _eventStore.GetStreamVersionAsync(streamId, cancellationToken);
            var targetVersion = expectedVersion ?? streamVersion;

            // Version alignment check
            if (state == null)
            {
                if (targetVersion >= 0)
                {
                    issues.Add($"State is null but expected version is {targetVersion}");
                }

                return EventStreamAlignmentResult.CreateMisaligned(
                    currentVersion: -1,
                    expectedVersion: targetVersion,
                    alignmentIssues: issues);
            }

            var stateVersion = GetStateVersion(state);

            // If projection is provided, we could do more detailed verification
            if (projection != null)
            {
                // For core functionality, just log that projection-based verification would happen here
                warnings.Add("Projection-based verification not implemented in core functionality");
            }

            if (stateVersion < targetVersion)
            {
                issues.Add($"State version {stateVersion} is behind expected version {targetVersion}");
            }
            else if (stateVersion > targetVersion)
            {
                issues.Add($"State version {stateVersion} is ahead of expected version {targetVersion}");
            }

            var isAligned = issues.Count == 0;

            return isAligned
                ? EventStreamAlignmentResult.CreateAligned(stateVersion, targetVersion)
                : EventStreamAlignmentResult.CreateMisaligned(stateVersion, targetVersion, issues);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error verifying event stream alignment for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to verify event stream alignment for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Performs comprehensive verification combining all consistency checks.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="version">The version to verify against</param>
    /// <param name="projection">The projection logic for state reconstruction</param>
    /// <param name="relatedGrains">Related grains to include in verification</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive verification result</returns>
    public async Task<ComprehensiveConsistencyResult> PerformComprehensiveVerificationAsync<T>(
        string grainId,
        string grainType,
        T state,
        long version,
        IEventProjection<T> projection,
        IReadOnlyList<string>? relatedGrains = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Perform individual verifications
            var consistencyResult = await VerifyConsistencyAsync(grainId, grainType, state, version, cancellationToken);
            var businessRulesResult = await ValidateBusinessRulesAsync(grainId, grainType, state, cancellationToken);
            var dataIntegrityResult = await VerifyDataIntegrityAsync(grainId, grainType, state, cancellationToken);
            var alignmentResult = await VerifyEventStreamAlignmentAsync(grainId, grainType, state, cancellationToken);

            // Calculate overall consistency
            var allIssues = new List<string>();
            allIssues.AddRange(consistencyResult.Issues);
            allIssues.AddRange(businessRulesResult.Violations);
            allIssues.AddRange(dataIntegrityResult.Issues);
            allIssues.AddRange(alignmentResult.Issues);

            var isOverallConsistent = allIssues.Count == 0;
            var consistencyScore = CalculateOverallConsistencyScore(consistencyResult, businessRulesResult, dataIntegrityResult, alignmentResult);

            return new ComprehensiveConsistencyResult
            {
                IsOverallConsistent = isOverallConsistent,
                BasicConsistency = consistencyResult,
                BusinessRuleValidation = businessRulesResult,
                DataIntegrityValidation = dataIntegrityResult,
                EventStreamAlignment = alignmentResult,
                CrossGrainConsistency = null, // For core functionality, skip cross-grain checks
                ConsistencyScore = consistencyScore,
                AllIssues = allIssues,
                Recommendations = GenerateRecommendations(allIssues),
                VerificationTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error performing comprehensive verification for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to perform comprehensive verification for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Verifies cross-grain consistency between related grains.
    /// </summary>
    /// <typeparam name="T">The type of grain state to verify</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being verified</param>
    /// <param name="state">The state to verify</param>
    /// <param name="relatedGrains">Related grains to check consistency against</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Cross-grain consistency verification result</returns>
    public Task<CrossGrainConsistencyResult> VerifyCrossGrainConsistencyAsync<T>(
        string grainId,
        string grainType,
        T state,
        IReadOnlyList<string>? relatedGrains = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            // For core functionality, skip complex cross-grain consistency checks
            // In a production system, this would verify relationships between grains

            if (relatedGrains == null || relatedGrains.Count == 0)
            {
                _logger.LogDebug("No related grains specified for cross-grain consistency check of grain {GrainId}", grainId);
                return Task.FromResult(CrossGrainConsistencyResult.Consistent(null));
            }

            _logger.LogDebug("Cross-grain consistency check requested for grain {GrainId} with {RelatedGrainCount} related grains (skipped in core implementation)",
                grainId, relatedGrains.Count);

            // For core functionality, assume consistency across grains
            // Advanced implementations would:
            // 1. Validate referential integrity between grains
            // 2. Check business rule consistency across grain boundaries
            // 3. Verify aggregate consistency constraints
            return Task.FromResult(CrossGrainConsistencyResult.Consistent(relatedGrains));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error verifying cross-grain consistency for grain {GrainId}", grainId);
            throw new StateConsistencyVerificationException(
                $"Failed to verify cross-grain consistency for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Gets consistency verification metrics and statistics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Verification metrics and statistics</returns>
    public Task<ConsistencyVerificationMetrics> GetVerificationMetricsAsync(CancellationToken cancellationToken = default)
    {
        // For core functionality, return empty metrics
        var metrics = ConsistencyVerificationMetrics.Empty();
        return Task.FromResult(metrics);
    }

    #region Private Helper Methods

    /// <summary>
    /// Extracts version information from state object using reflection.
    /// </summary>
    /// <typeparam name="T">The type of state</typeparam>
    /// <param name="state">The state object</param>
    /// <returns>The version number, or -1 if not found</returns>
    private static long GetStateVersion<T>(T state)
    {
        if (state == null)
        {
            return -1;
        }

        // Try to get version using reflection
        var type = typeof(T);
        var versionProperty = type.GetProperty("Version") ??
                             type.GetProperty("StateVersion") ??
                             type.GetProperty("EntityVersion");

        if (versionProperty?.PropertyType == typeof(long))
        {
            return (long)(versionProperty.GetValue(state) ?? -1L);
        }

        if (versionProperty?.PropertyType == typeof(int))
        {
            return (int)(versionProperty.GetValue(state) ?? -1);
        }

        return 0; // Default version if not found
    }

    /// <summary>
    /// Checks for potential circular references in the state object.
    /// </summary>
    /// <typeparam name="T">The type of state</typeparam>
    /// <param name="state">The state object</param>
    /// <returns>True if circular references are detected</returns>
    private static bool HasCircularReferences<T>(T state)
    {
        if (state == null)
        {
            return false;
        }

        try
        {
            // Simple check by attempting JSON serialization without circular reference handling
            System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions
            {
                ReferenceHandler = null // No circular reference handling
            });
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
        catch
        {
            return false; // Other exceptions don't indicate circular references
        }
    }

    /// <summary>
    /// Performs grain-type specific business rule validations.
    /// </summary>
    /// <typeparam name="T">The type of state</typeparam>
    /// <param name="grainType">The type of grain</param>
    /// <param name="state">The state object</param>
    /// <param name="violations">List to add violations to</param>
    /// <param name="warnings">List to add warnings to</param>
    private static void ValidateGrainSpecificRules<T>(
        string grainType,
        T state,
        List<string> violations,
        List<string> warnings)
    {
        // Basic grain-type specific validations
        // More specific rules would be implemented based on actual grain types in the system

        if (grainType.Contains("Chat", StringComparison.OrdinalIgnoreCase))
        {
            // Chat-specific validations
            ValidateChatGrainRules(state, violations, warnings);
        }
        else if (grainType.Contains("User", StringComparison.OrdinalIgnoreCase))
        {
            // User-specific validations
            ValidateUserGrainRules(state, violations, warnings);
        }
    }

    /// <summary>
    /// Validates chat grain specific business rules.
    /// </summary>
    private static void ValidateChatGrainRules<T>(
        T state,
        List<string> violations,
        List<string> warnings)
    {
        var stateType = typeof(T);

        // Check for chat ID property
        var chatIdProperty = stateType.GetProperty("ChatId") ?? stateType.GetProperty("Id");
        if (chatIdProperty != null)
        {
            var chatId = chatIdProperty.GetValue(state) as string;
            if (string.IsNullOrWhiteSpace(chatId))
            {
                violations.Add("Chat ID cannot be null or empty");
            }
        }
    }

    /// <summary>
    /// Validates user grain specific business rules.
    /// </summary>
    private static void ValidateUserGrainRules<T>(
        T state,
        List<string> violations,
        List<string> warnings)
    {
        var stateType = typeof(T);

        // Check for user ID property
        var userIdProperty = stateType.GetProperty("UserId") ?? stateType.GetProperty("Id");
        if (userIdProperty != null)
        {
            var userId = userIdProperty.GetValue(state) as string;
            if (string.IsNullOrWhiteSpace(userId))
            {
                violations.Add("User ID cannot be null or empty");
            }
        }
    }

    /// <summary>
    /// Calculates confidence score based on verification results.
    /// </summary>
    private static double CalculateConfidenceScore(
        DataIntegrityVerificationResult dataIntegrityResult,
        BusinessRuleValidationResult businessRulesResult,
        EventStreamAlignmentResult alignmentResult)
    {
        double score = 1.0;

        // Reduce score for data integrity issues
        if (!dataIntegrityResult.IsValid)
        {
            score -= 0.4; // Major reduction for data integrity issues
        }
        else if (dataIntegrityResult.Warnings.Any())
        {
            score -= 0.1; // Minor reduction for warnings
        }

        // Reduce score for business rule violations
        if (!businessRulesResult.IsValid)
        {
            score -= 0.3; // Significant reduction for business rule violations
        }
        else if (businessRulesResult.Warnings.Any())
        {
            score -= 0.1; // Minor reduction for warnings
        }

        // Reduce score for event stream alignment issues
        if (!alignmentResult.IsAligned)
        {
            score -= 0.3; // Significant reduction for alignment issues
        }
        else if (alignmentResult.Warnings.Any())
        {
            score -= 0.05; // Very minor reduction for warnings
        }

        return Math.Max(0.0, score);
    }

    /// <summary>
    /// Calculates overall consistency score based on all verification results.
    /// </summary>
    private static double CalculateOverallConsistencyScore(
        ConsistencyVerificationResult consistencyResult,
        BusinessRuleValidationResult businessRulesResult,
        DataIntegrityVerificationResult dataIntegrityResult,
        EventStreamAlignmentResult alignmentResult)
    {
        double score = 100.0;

        // Reduce score based on each type of issue
        if (!consistencyResult.IsConsistent)
        {
            score -= 30.0; // Major reduction for general consistency issues
        }

        if (!businessRulesResult.IsValid)
        {
            score -= 25.0; // Significant reduction for business rule violations
        }

        if (!dataIntegrityResult.IsValid)
        {
            score -= 35.0; // Major reduction for data integrity issues
        }

        if (!alignmentResult.IsAligned)
        {
            score -= 20.0; // Moderate reduction for alignment issues
        }

        // Additional reductions for warnings
        var totalWarnings = consistencyResult.Warnings.Count +
                          dataIntegrityResult.Warnings.Count +
                          alignmentResult.Warnings.Count;

        score -= totalWarnings * 2.0; // Minor reduction per warning

        return Math.Max(0.0, Math.Min(100.0, score));
    }

    /// <summary>
    /// Generates recommendations based on identified issues.
    /// </summary>
    private static IReadOnlyList<string> GenerateRecommendations(List<string> allIssues)
    {
        var recommendations = new List<string>();

        if (allIssues.Count == 0)
        {
            recommendations.Add("State appears to be consistent. Continue monitoring for any changes.");
            return recommendations;
        }

        // Generate context-aware recommendations
        if (allIssues.Any(i => i.Contains("null", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Address null state values by initializing with default values or recovering from event stream");
        }

        if (allIssues.Any(i => i.Contains("version", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Version mismatch detected. Consider performing state recovery from event stream");
        }

        if (allIssues.Any(i => i.Contains("serialization", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Data serialization issues found. Verify state object structure and compatibility");
        }

        if (allIssues.Any(i => i.Contains("stream", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Event stream alignment issues. Consider full state reconstruction from events");
        }

        if (allIssues.Any(i => i.Contains("ID", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Identifier validation failed. Ensure all required IDs are properly set");
        }

        // Default recommendation if no specific patterns match
        if (recommendations.Count == 0)
        {
            recommendations.Add("Multiple consistency issues detected. Consider performing comprehensive state recovery");
        }

        return recommendations;
    }

    /// <summary>
    /// Computes a simple hash of the input string for integrity verification.
    /// </summary>
    private static string ComputeSimpleHash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Use SHA256 for basic hash computation
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes);
    }

    #endregion
}