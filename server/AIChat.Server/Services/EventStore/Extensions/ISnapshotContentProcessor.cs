namespace AIChat.Server.Services.EventStore.Extensions;

/// <summary>
/// Interface for processing snapshot content (compression, encryption, etc.).
/// Enables extension of content processing capabilities without modifying core storage logic.
/// Follows Open/Closed Principle for content transformation extensions.
/// </summary>
public interface ISnapshotContentProcessor
{
    /// <summary>
    /// Gets the name of this content processor.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the content type identifier for this processor.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Gets whether this processor supports the specified content type.
    /// </summary>
    /// <param name="contentType">The content type to check</param>
    /// <returns>True if this processor can handle the content type</returns>
    bool SupportsContentType(string contentType);

    /// <summary>
    /// Processes (compresses/transforms) the input data.
    /// </summary>
    /// <param name="inputData">The raw data to process</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Processed data with metadata about the transformation</returns>
    Task<ProcessedContent> ProcessAsync(byte[] inputData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the processing (decompresses/transforms back) the data.
    /// </summary>
    /// <param name="processedData">The processed data to reverse</param>
    /// <param name="originalContentType">The original content type before processing</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Original data restored from processed form</returns>
    Task<byte[]> ReverseProcessAsync(byte[] processedData, string originalContentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that processed data can be successfully reversed.
    /// </summary>
    /// <param name="processedData">The processed data to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result indicating integrity</returns>
    Task<ContentValidationResult> ValidateAsync(byte[] processedData, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of content processing.
/// </summary>
public record ProcessedContent
{
    /// <summary>
    /// Gets the processed data.
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// Gets the content type identifier after processing.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Gets the original size before processing.
    /// </summary>
    public required long OriginalSize { get; init; }

    /// <summary>
    /// Gets the size after processing.
    /// </summary>
    public required long ProcessedSize { get; init; }

    /// <summary>
    /// Gets the compression/transformation ratio achieved.
    /// </summary>
    public double CompressionRatio => OriginalSize > 0 && ProcessedSize > 0 ? (double)OriginalSize / ProcessedSize : 1.0;

    /// <summary>
    /// Gets metadata about the processing operation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Gets the processing time taken.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// Represents the result of content validation.
/// </summary>
public record ContentValidationResult
{
    /// <summary>
    /// Gets whether the content is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets any validation issues found.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Gets the content hash if validation succeeded.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Gets additional validation metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="contentHash">The validated content hash</param>
    /// <param name="metadata">Optional validation metadata</param>
    /// <returns>A successful validation result</returns>
    public static ContentValidationResult CreateValid(string contentHash, Dictionary<string, object>? metadata = null)
    {
        return new ContentValidationResult
        {
            IsValid = true,
            ContentHash = contentHash,
            Metadata = metadata ?? []
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="issues">The validation issues</param>
    /// <param name="metadata">Optional validation metadata</param>
    /// <returns>A failed validation result</returns>
    public static ContentValidationResult CreateInvalid(IReadOnlyList<string> issues, Dictionary<string, object>? metadata = null)
    {
        return new ContentValidationResult
        {
            IsValid = false,
            Issues = issues,
            Metadata = metadata ?? []
        };
    }
}