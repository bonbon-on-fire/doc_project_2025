using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// Query operations for SqliteSnapshotStore.
/// Handles flexible snapshot querying with filtering, sorting, and pagination.
/// </summary>
public sealed partial class SqliteSnapshotStore : ISnapshotQuery
{
    #region ISnapshotQuery Implementation

    /// <summary>
    /// Queries snapshots using flexible criteria.
    /// </summary>
    public async Task<SnapshotQueryResult> QuerySnapshotsAsync(SnapshotQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (isValid, error) = query.Validate();
        if (!isValid)
        {
            throw SnapshotStoreException.ValidationError(error!);
        }

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Querying snapshots with criteria: StreamId={StreamId}, StateType={StateType}, FromTimestamp={FromTimestamp}, ToTimestamp={ToTimestamp}",
                query.StreamId, query.StateType, query.FromTimestamp, query.ToTimestamp);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Build the query SQL and parameters
            var (sql, parameters) = BuildQuerySql(query);

            var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var snapshots = new List<SnapshotMetadata>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    snapshots.Add(ReadSnapshotMetadata(reader));
                }
            }

            // Get total count for pagination
            var totalCount = await GetQueryTotalCountAsync(connection, query, cancellationToken);

            _logger.LogDebug("Query returned {Count} snapshots out of {TotalCount} total matches",
                snapshots.Count, totalCount);

            _metrics.RecordQuerySuccess();
            return SnapshotQueryResult.Create(
                snapshots,
                totalCount,
                query.Page,
                query.PageSize,
                new Dictionary<string, object>
                {
                    ["queryExecutionTimeMs"] = 0 // Duration tracking handled by metrics collector
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute snapshot query");
            _metrics.RecordQueryFailure();
            throw new SnapshotStoreException(
                "Failed to execute snapshot query",
                SnapshotErrorCode.InternalError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets the complete snapshot history for a stream.
    /// </summary>
    public async Task<SnapshotQueryResult> GetSnapshotHistoryAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        var query = SnapshotQuery.ForStream(streamId, pageSize: 1000);
        query = query with { SortOrder = SnapshotSortOrder.VersionAscending };

        return await QuerySnapshotsAsync(query, cancellationToken);
    }

    /// <summary>
    /// Gets snapshots within a time range across all streams.
    /// </summary>
    public async Task<SnapshotQueryResult> GetSnapshotsByTimeRangeAsync(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        CancellationToken cancellationToken = default)
    {
        if (fromTimestamp > toTimestamp)
        {
            throw new ArgumentException("fromTimestamp cannot be greater than toTimestamp");
        }

        var query = SnapshotQuery.ForTimeRange(fromTimestamp, toTimestamp, pageSize: 1000);
        return await QuerySnapshotsAsync(query, cancellationToken);
    }

    /// <summary>
    /// Gets snapshots that are eligible for cleanup based on retention policies.
    /// </summary>
    public async Task<SnapshotQueryResult> GetSnapshotsForCleanupAsync(
        SnapshotRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retentionPolicy);

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Getting snapshots for cleanup based on retention policy");

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var conditions = new List<string>();
            var parameters = new Dictionary<string, object>();

            // Age-based cleanup
            if (retentionPolicy.MaxAge.HasValue)
            {
                var cutoffTime = DateTimeOffset.UtcNow.Subtract(retentionPolicy.MaxAge.Value);
                conditions.Add("s.Timestamp < @cutoffTime");
                parameters["@cutoffTime"] = cutoffTime.ToString("O", CultureInfo.InvariantCulture);
            }

            // Count-based cleanup (keep only the newest N snapshots per stream)
            if (retentionPolicy.MaxSnapshotsPerStream.HasValue)
            {
                conditions.Add(@"
                    s.Id NOT IN (
                        SELECT Id FROM Snapshots s2
                        WHERE s2.StreamId = s.StreamId
                        ORDER BY s2.Version DESC
                        LIMIT @maxSnapshotsPerStream
                    )");
                parameters["@maxSnapshotsPerStream"] = retentionPolicy.MaxSnapshotsPerStream.Value;
            }

            // If no conditions, return empty result
            if (conditions.Count == 0)
            {
                return SnapshotQueryResult.Empty();
            }

            var sql = $@"
                SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                       s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata
                FROM Snapshots s
                WHERE {string.Join(" OR ", conditions)}
                ORDER BY s.Timestamp ASC";

            var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var snapshots = new List<SnapshotMetadata>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    snapshots.Add(ReadSnapshotMetadata(reader));
                }
            }

            _logger.LogDebug("Found {Count} snapshots eligible for cleanup", snapshots.Count);

            _metrics.RecordQuerySuccess();
            return SnapshotQueryResult.Create(
                snapshots,
                snapshots.Count,
                1,
                snapshots.Count,
                new Dictionary<string, object>
                {
                    ["retentionPolicy"] = retentionPolicy,
                    ["queryExecutionTimeMs"] = 0 // Duration tracking handled by metrics collector
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get snapshots for cleanup");
            _metrics.RecordQueryFailure();
            throw new SnapshotStoreException(
                "Failed to get snapshots for cleanup",
                SnapshotErrorCode.InternalError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets snapshots grouped by content hash for deduplication analysis.
    /// </summary>
    public async Task<Dictionary<string, List<SnapshotMetadata>>> GetSnapshotsByContentHashAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Getting snapshots grouped by content hash for deduplication analysis");

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                       s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata
                FROM Snapshots s
                WHERE s.ContentHash IN (
                    SELECT ContentHash
                    FROM Snapshots
                    GROUP BY ContentHash
                    HAVING COUNT(*) > 1
                )
                ORDER BY s.ContentHash, s.Timestamp";

            var result = new Dictionary<string, List<SnapshotMetadata>>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var metadata = ReadSnapshotMetadata(reader);
                if (!result.TryGetValue(metadata.ContentHash, out var value))
                {
                    value = [];
                    result[metadata.ContentHash] = value;
                }

                value.Add(metadata);
            }

            _logger.LogDebug("Found {HashCount} content hashes with {DuplicateCount} duplicate snapshots",
                result.Count, result.Values.Sum(list => list.Count));

            _metrics.RecordQuerySuccess();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get snapshots by content hash");
            _metrics.RecordQueryFailure();
            throw new SnapshotStoreException(
                "Failed to get snapshots by content hash",
                SnapshotErrorCode.InternalError,
                innerException: ex);
        }
    }

    #endregion

    #region Query Helper Methods

    /// <summary>
    /// Builds the SQL query and parameters for a snapshot query.
    /// </summary>
    private static (string sql, Dictionary<string, object> parameters) BuildQuerySql(SnapshotQuery query)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        // Stream filter
        if (!string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.StreamId = @streamId");
            parameters["@streamId"] = query.StreamId;
        }

        // State type filter
        if (!string.IsNullOrEmpty(query.StateType))
        {
            conditions.Add("s.StateType = @stateType");
            parameters["@stateType"] = query.StateType;
        }

        // Timestamp range filter
        if (query.FromTimestamp.HasValue)
        {
            conditions.Add("s.Timestamp >= @fromTimestamp");
            parameters["@fromTimestamp"] = query.FromTimestamp.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (query.ToTimestamp.HasValue)
        {
            conditions.Add("s.Timestamp <= @toTimestamp");
            parameters["@toTimestamp"] = query.ToTimestamp.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        // Version range filter (only valid with StreamId)
        if (query.FromVersion.HasValue && !string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.Version >= @fromVersion");
            parameters["@fromVersion"] = query.FromVersion.Value;
        }

        if (query.ToVersion.HasValue && !string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.Version <= @toVersion");
            parameters["@toVersion"] = query.ToVersion.Value;
        }

        // Content hash filter
        if (!string.IsNullOrEmpty(query.ContentHash))
        {
            conditions.Add("s.ContentHash = @contentHash");
            parameters["@contentHash"] = query.ContentHash;
        }

        // Size filters
        if (query.MinCompressedSize.HasValue)
        {
            conditions.Add("s.CompressedSize >= @minCompressedSize");
            parameters["@minCompressedSize"] = query.MinCompressedSize.Value;
        }

        if (query.MaxCompressedSize.HasValue)
        {
            conditions.Add("s.CompressedSize <= @maxCompressedSize");
            parameters["@maxCompressedSize"] = query.MaxCompressedSize.Value;
        }

        // Compression type filter
        if (!string.IsNullOrEmpty(query.CompressionType))
        {
            conditions.Add("s.CompressionType = @compressionType");
            parameters["@compressionType"] = query.CompressionType;
        }

        // Build WHERE clause
        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

        // Build ORDER BY clause
        var orderByClause = BuildOrderByClause(query.SortOrder);

        // Build pagination
        var offset = (query.Page - 1) * query.PageSize;
        parameters["@limit"] = query.PageSize;
        parameters["@offset"] = offset;

        var sql = $@"
            SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                   s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata
            FROM Snapshots s
            {whereClause}
            {orderByClause}
            LIMIT @limit OFFSET @offset";

        return (sql, parameters);
    }

    /// <summary>
    /// Builds the ORDER BY clause for a query.
    /// </summary>
    private static string BuildOrderByClause(SnapshotSortOrder sortOrder)
    {
        return sortOrder switch
        {
            SnapshotSortOrder.TimestampAscending => "ORDER BY s.Timestamp ASC",
            SnapshotSortOrder.TimestampDescending => "ORDER BY s.Timestamp DESC",
            SnapshotSortOrder.VersionAscending => "ORDER BY s.Version ASC",
            SnapshotSortOrder.VersionDescending => "ORDER BY s.Version DESC",
            SnapshotSortOrder.SizeAscending => "ORDER BY s.CompressedSize ASC",
            SnapshotSortOrder.SizeDescending => "ORDER BY s.CompressedSize DESC",
            SnapshotSortOrder.CompressionRatioAscending => "ORDER BY (CAST(s.CompressedSize AS REAL) / s.UncompressedSize) ASC",
            SnapshotSortOrder.CompressionRatioDescending => "ORDER BY (CAST(s.CompressedSize AS REAL) / s.UncompressedSize) DESC",
            _ => "ORDER BY s.Timestamp DESC"
        };
    }

    /// <summary>
    /// Gets the total count of snapshots matching the query criteria.
    /// </summary>
    private static async Task<int> GetQueryTotalCountAsync(
        SqliteConnection connection,
        SnapshotQuery query,
        CancellationToken cancellationToken)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        // Build the same conditions as the main query (without pagination)
        if (!string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.StreamId = @streamId");
            parameters["@streamId"] = query.StreamId;
        }

        if (!string.IsNullOrEmpty(query.StateType))
        {
            conditions.Add("s.StateType = @stateType");
            parameters["@stateType"] = query.StateType;
        }

        if (query.FromTimestamp.HasValue)
        {
            conditions.Add("s.Timestamp >= @fromTimestamp");
            parameters["@fromTimestamp"] = query.FromTimestamp.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (query.ToTimestamp.HasValue)
        {
            conditions.Add("s.Timestamp <= @toTimestamp");
            parameters["@toTimestamp"] = query.ToTimestamp.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (query.FromVersion.HasValue && !string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.Version >= @fromVersion");
            parameters["@fromVersion"] = query.FromVersion.Value;
        }

        if (query.ToVersion.HasValue && !string.IsNullOrEmpty(query.StreamId))
        {
            conditions.Add("s.Version <= @toVersion");
            parameters["@toVersion"] = query.ToVersion.Value;
        }

        if (!string.IsNullOrEmpty(query.ContentHash))
        {
            conditions.Add("s.ContentHash = @contentHash");
            parameters["@contentHash"] = query.ContentHash;
        }

        if (query.MinCompressedSize.HasValue)
        {
            conditions.Add("s.CompressedSize >= @minCompressedSize");
            parameters["@minCompressedSize"] = query.MinCompressedSize.Value;
        }

        if (query.MaxCompressedSize.HasValue)
        {
            conditions.Add("s.CompressedSize <= @maxCompressedSize");
            parameters["@maxCompressedSize"] = query.MaxCompressedSize.Value;
        }

        if (!string.IsNullOrEmpty(query.CompressionType))
        {
            conditions.Add("s.CompressionType = @compressionType");
            parameters["@compressionType"] = query.CompressionType;
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

        var countSql = $@"
            SELECT COUNT(*)
            FROM Snapshots s
            {whereClause}";

        var countCommand = connection.CreateCommand();
        countCommand.CommandText = countSql;

        foreach (var (name, value) in parameters)
        {
            countCommand.Parameters.AddWithValue(name, value);
        }

        var result = await countCommand.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)count : 0;
    }

    #endregion
}