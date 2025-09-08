using System.Text;
using System.Text.Json;
using AIChat.Server.Services.Streaming.Abstractions;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// File-based implementation of persistent buffer storage.
/// Uses JSON serialization for human readability and debugging.
/// </summary>
public sealed class FileBasedBufferStore : IPersistentBufferStore, IDisposable
{
    private readonly ILogger<FileBasedBufferStore> _logger;
    private readonly FileBasedBufferStoreOptions _options;
    private readonly SemaphoreSlim _accessLock;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the FileBasedBufferStore class.
    /// </summary>
    public FileBasedBufferStore(
        ILogger<FileBasedBufferStore> logger,
        IOptions<FileBasedBufferStoreOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _accessLock = new SemaphoreSlim(1, 1);

        // Configure JSON serialization
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Ensure storage directory exists
        EnsureStorageDirectory();
    }

    /// <inheritdoc/>
    public async Task<bool> PersistBufferAsync(
        string streamId,
        IEnumerable<BufferedStreamMessage> messages,
        BufferMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(messages);

        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageList = messages.ToList();
            if (messageList.Count == 0)
            {
                _logger.LogDebug("No messages to persist for stream {StreamId}", streamId);
                return true;
            }

            // Create stream directory if it doesn't exist
            var streamDir = GetStreamDirectory(streamId);
            _ = Directory.CreateDirectory(streamDir);

            // Generate file name with timestamp
            var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
            var filePath = Path.Combine(streamDir, fileName);

            // Create persistence data
            var persistenceData = new PersistedBufferData
            {
                StreamId = streamId,
                Messages = messageList,
                Metadata = metadata ?? CreateMetadata(streamId, messageList),
                PersistedAt = DateTime.UtcNow,
                FormatVersion = "1.0"
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(persistenceData, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Write to file atomically
            var tempPath = filePath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, true);

            _logger.LogInformation(
                "Persisted {MessageCount} messages for stream {StreamId} to {FilePath}",
                messageList.Count, streamId, filePath);

            // Clean up old files if rotation is enabled
            if (_options.EnableRotation)
            {
                await CleanupOldFilesAsync(streamDir, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist buffer for stream {StreamId}", streamId);
            return false;
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PersistedBuffer?> LoadBufferAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var streamDir = GetStreamDirectory(streamId);
            if (!Directory.Exists(streamDir))
            {
                _logger.LogDebug("No persisted buffer found for stream {StreamId}", streamId);
                return null;
            }

            // Get all buffer files sorted by creation time (newest first)
            var files = Directory.GetFiles(streamDir, "*.json")
                .OrderByDescending(f => new FileInfo(f).CreationTimeUtc)
                .ToList();

            if (files.Count == 0)
            {
                _logger.LogDebug("No buffer files found for stream {StreamId}", streamId);
                return null;
            }

            var allMessages = new List<BufferedStreamMessage>();
            BufferMetadata? latestMetadata = null;
            var corruptedFiles = new List<string>();

            // Load messages from all files
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var data = JsonSerializer.Deserialize<PersistedBufferData>(json, _jsonOptions);

                    if (data != null)
                    {
                        allMessages.AddRange(data.Messages);
                        if (latestMetadata == null || data.Metadata.LastUpdatedAt > latestMetadata.LastUpdatedAt)
                        {
                            latestMetadata = data.Metadata;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load buffer file {FilePath}", file);
                    corruptedFiles.Add(file);
                }
            }

            if (allMessages.Count == 0 && latestMetadata == null)
            {
                _logger.LogWarning("All buffer files for stream {StreamId} are corrupted", streamId);
                return new PersistedBuffer
                {
                    StreamId = streamId,
                    Messages = Array.Empty<BufferedStreamMessage>(),
                    Metadata = CreateMetadata(streamId, Array.Empty<BufferedStreamMessage>()),
                    IsCorrupted = true,
                    CorruptionDetails = $"All {corruptedFiles.Count} files are corrupted"
                };
            }

            // Sort messages by sequence number and remove duplicates
            var uniqueMessages = allMessages
                .GroupBy(m => m.SequenceNumber)
                .Select(g => g.First())
                .OrderBy(m => m.SequenceNumber)
                .ToList();

            _logger.LogInformation(
                "Loaded {MessageCount} unique messages for stream {StreamId} from {FileCount} files",
                uniqueMessages.Count, streamId, files.Count);

            return new PersistedBuffer
            {
                StreamId = streamId,
                Messages = uniqueMessages,
                Metadata = latestMetadata ?? CreateMetadata(streamId, uniqueMessages),
                IsCorrupted = corruptedFiles.Count > 0,
                CorruptionDetails = corruptedFiles.Count > 0
                    ? $"{corruptedFiles.Count} corrupted files detected"
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load buffer for stream {StreamId}", streamId);
            return null;
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteBufferAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var streamDir = GetStreamDirectory(streamId);
            if (!Directory.Exists(streamDir))
            {
                return false;
            }

            Directory.Delete(streamDir, recursive: true);
            _logger.LogInformation("Deleted persisted buffer for stream {StreamId}", streamId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete buffer for stream {StreamId}", streamId);
            return false;
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListPersistedBuffersAsync(CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_options.StoragePath))
            {
                return Array.Empty<string>();
            }

            var streamIds = Directory.GetDirectories(_options.StoragePath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            return streamIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list persisted buffers");
            return Array.Empty<string>();
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<BufferMetadata?> GetBufferMetadataAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var streamDir = GetStreamDirectory(streamId);
            if (!Directory.Exists(streamDir))
            {
                return null;
            }

            // Get the most recent metadata file
            var metadataFile = Path.Combine(streamDir, "metadata.json");
            if (File.Exists(metadataFile))
            {
                var json = await File.ReadAllTextAsync(metadataFile, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<BufferMetadata>(json, _jsonOptions);
            }

            // Fall back to loading from buffer files
            var files = Directory.GetFiles(streamDir, "*.json")
                .Where(f => !f.EndsWith("metadata.json"))
                .OrderByDescending(f => new FileInfo(f).CreationTimeUtc)
                .FirstOrDefault();

            if (files != null)
            {
                var json = await File.ReadAllTextAsync(files, cancellationToken).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<PersistedBufferData>(json, _jsonOptions);
                return data?.Metadata;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for stream {StreamId}", streamId);
            return null;
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<int> CleanupExpiredBuffersAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_options.StoragePath))
            {
                return 0;
            }

            var cutoffTime = DateTime.UtcNow - retentionPeriod;
            var deletedCount = 0;

            foreach (var streamDir in Directory.GetDirectories(_options.StoragePath))
            {
                var dirInfo = new DirectoryInfo(streamDir);

                // Check if all files in the directory are expired
                var files = dirInfo.GetFiles("*.json");
                if (files.Length > 0 && files.All(f => f.LastWriteTimeUtc < cutoffTime))
                {
                    try
                    {
                        Directory.Delete(streamDir, recursive: true);
                        deletedCount++;
                        _logger.LogInformation("Cleaned up expired buffer for stream {StreamId}", Path.GetFileName(streamDir));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete expired buffer directory {Path}", streamDir);
                    }
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired buffers");
            return 0;
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PersistenceStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_options.StoragePath))
            {
                return new PersistenceStatistics
                {
                    TotalBuffers = 0,
                    TotalSizeBytes = 0,
                    TotalMessages = 0,
                    StoragePath = _options.StoragePath
                };
            }

            var stats = new PersistenceStatistics
            {
                TotalBuffers = 0,
                TotalSizeBytes = 0,
                TotalMessages = 0,
                StoragePath = _options.StoragePath
            };

            var streamDirs = Directory.GetDirectories(_options.StoragePath);
            stats = stats with { TotalBuffers = streamDirs.Length };

            DateTime? oldestTimestamp = null;
            DateTime? newestTimestamp = null;
            long largestSize = 0;
            long totalMessages = 0;
            int corruptedCount = 0;

            foreach (var streamDir in streamDirs)
            {
                var dirInfo = new DirectoryInfo(streamDir);
                var files = dirInfo.GetFiles("*.json");

                foreach (var file in files)
                {
                    stats = stats with { TotalSizeBytes = stats.TotalSizeBytes + file.Length };

                    if (file.Length > largestSize)
                    {
                        largestSize = file.Length;
                    }

                    var timestamp = file.LastWriteTimeUtc;
                    if (!oldestTimestamp.HasValue || timestamp < oldestTimestamp)
                    {
                        oldestTimestamp = timestamp;
                    }
                    if (!newestTimestamp.HasValue || timestamp > newestTimestamp)
                    {
                        newestTimestamp = timestamp;
                    }

                    // Try to count messages
                    try
                    {
                        var json = await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false);
                        var data = JsonSerializer.Deserialize<PersistedBufferData>(json, _jsonOptions);
                        if (data != null)
                        {
                            totalMessages += data.Messages.Count;
                        }
                    }
                    catch
                    {
                        corruptedCount++;
                    }
                }
            }

            // Get available storage space
            var driveInfo = new DriveInfo(Path.GetPathRoot(_options.StoragePath)!);

            return stats with
            {
                OldestBufferTimestamp = oldestTimestamp,
                NewestBufferTimestamp = newestTimestamp,
                LargestBufferSizeBytes = largestSize,
                TotalMessages = totalMessages,
                CorruptedBuffers = corruptedCount,
                AvailableStorageBytes = driveInfo.AvailableFreeSpace
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get persistence statistics");
            return new PersistenceStatistics
            {
                TotalBuffers = 0,
                TotalSizeBytes = 0,
                TotalMessages = 0,
                StoragePath = _options.StoragePath
            };
        }
        finally
        {
            _ = _accessLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if storage path exists and is writable
            if (!Directory.Exists(_options.StoragePath))
            {
                _ = Directory.CreateDirectory(_options.StoragePath);
            }

            // Try to write a test file
            var testFile = Path.Combine(_options.StoragePath, $".health_check_{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(testFile, DateTime.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
            File.Delete(testFile);

            // Check available space
            var driveInfo = new DriveInfo(Path.GetPathRoot(_options.StoragePath)!);
            if (driveInfo.AvailableFreeSpace < _options.MinimumFreeSpaceBytes)
            {
                _logger.LogWarning(
                    "Low disk space. Available: {AvailableBytes}, Required: {RequiredBytes}",
                    driveInfo.AvailableFreeSpace, _options.MinimumFreeSpaceBytes);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for persistence store");
            return false;
        }
    }

    /// <summary>
    /// Ensures the storage directory exists.
    /// </summary>
    private void EnsureStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(_options.StoragePath))
            {
                _ = Directory.CreateDirectory(_options.StoragePath);
                _logger.LogInformation("Created storage directory at {Path}", _options.StoragePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create storage directory at {Path}", _options.StoragePath);
            throw;
        }
    }

    /// <summary>
    /// Gets the directory path for a stream.
    /// </summary>
    private string GetStreamDirectory(string streamId)
    {
        // Sanitize stream ID for file system
        var safeStreamId = string.Join("_", streamId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_options.StoragePath, safeStreamId);
    }

    /// <summary>
    /// Creates metadata for a buffer.
    /// </summary>
    private static BufferMetadata CreateMetadata(string streamId, IReadOnlyList<BufferedStreamMessage> messages)
    {
        var now = DateTime.UtcNow;
        var totalSize = messages.Sum(m => (long)m.SizeBytes);

        return new BufferMetadata
        {
            StreamId = streamId,
            CreatedAt = now,
            LastUpdatedAt = now,
            MessageCount = messages.Count,
            TotalSizeBytes = totalSize,
            FirstSequenceNumber = messages.FirstOrDefault()?.SequenceNumber,
            LastSequenceNumber = messages.LastOrDefault()?.SequenceNumber,
            FormatVersion = "1.0",
            IsCompressed = false,
            IsEncrypted = false
        };
    }

    /// <summary>
    /// Cleans up old files in a stream directory based on rotation settings.
    /// </summary>
    private Task CleanupOldFilesAsync(string streamDir, CancellationToken cancellationToken)
    {
        try
        {
            var files = Directory.GetFiles(streamDir, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(_options.MaxFilesPerStream)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                    _logger.LogDebug("Deleted old buffer file {FilePath}", file.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old buffer file {FilePath}", file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old files in {Directory}", streamDir);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _accessLock?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Internal class for persisted buffer data.
    /// </summary>
    private sealed class PersistedBufferData
    {
        public required string StreamId { get; init; }
        public required List<BufferedStreamMessage> Messages { get; init; }
        public required BufferMetadata Metadata { get; init; }
        public required DateTime PersistedAt { get; init; }
        public required string FormatVersion { get; init; }
    }
}

/// <summary>
/// Configuration options for FileBasedBufferStore.
/// </summary>
public sealed class FileBasedBufferStoreOptions
{
    /// <summary>
    /// Gets or sets the storage path for buffer files.
    /// </summary>
    public string StoragePath { get; set; } = Path.Combine("data", "buffers");

    /// <summary>
    /// Gets or sets whether to enable file rotation.
    /// </summary>
    public bool EnableRotation { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of files per stream.
    /// </summary>
    public int MaxFilesPerStream { get; set; } = 10;

    /// <summary>
    /// Gets or sets the minimum free space required in bytes.
    /// </summary>
    public long MinimumFreeSpaceBytes { get; set; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>
    /// Gets or sets whether to compress old files.
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Gets or sets the compression threshold in days.
    /// </summary>
    public int CompressionThresholdDays { get; set; } = 7;
}
