using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Interface for serializing and deserializing events.
/// Provides consistent event serialization across different storage implementations.
/// </summary>
public interface IEventSerializer
{
    /// <summary>
    /// Serializes an event to a string representation.
    /// </summary>
    /// <param name="eventData">The event to serialize</param>
    /// <returns>The serialized event data</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventData is null</exception>
    /// <exception cref="EventSerializationException">Thrown when serialization fails</exception>
    string Serialize(IEvent eventData);

    /// <summary>
    /// Deserializes an event from a string representation.
    /// </summary>
    /// <param name="eventType">The type of event to deserialize</param>
    /// <param name="eventData">The serialized event data</param>
    /// <returns>The deserialized event</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventType or eventData is null</exception>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    IEvent Deserialize(string eventType, string eventData);

    /// <summary>
    /// Deserializes an event to a specific type.
    /// </summary>
    /// <typeparam name="T">The specific event type</typeparam>
    /// <param name="eventData">The serialized event data</param>
    /// <returns>The deserialized event</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventData is null</exception>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    T Deserialize<T>(string eventData) where T : IEvent;

    /// <summary>
    /// Serializes event metadata to a string representation.
    /// </summary>
    /// <param name="metadata">The metadata to serialize</param>
    /// <returns>The serialized metadata, or null if metadata is null</returns>
    /// <exception cref="EventSerializationException">Thrown when serialization fails</exception>
    string? SerializeMetadata(Dictionary<string, object>? metadata);

    /// <summary>
    /// Deserializes event metadata from a string representation.
    /// </summary>
    /// <param name="metadataJson">The serialized metadata</param>
    /// <returns>The deserialized metadata, or null if metadataJson is null</returns>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    Dictionary<string, object>? DeserializeMetadata(string? metadataJson);

    /// <summary>
    /// Gets the content type used by this serializer.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Gets whether this serializer supports the given event type.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>True if the event type is supported</returns>
    bool SupportsEventType(string eventType);
}

/// <summary>
/// JSON-based event serializer using System.Text.Json with Orleans compatibility.
/// Provides high-performance serialization optimized for event sourcing scenarios.
/// </summary>
public class JsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = CreateSerializerOptions();
    private readonly JsonSerializerOptions _options;
    private readonly ILogger<JsonEventSerializer>? _logger;
    private readonly Dictionary<string, Type> _eventTypeRegistry;

    /// <summary>
    /// Gets the content type for JSON serialization.
    /// </summary>
    public string ContentType => "application/json";

    /// <summary>
    /// Initializes a new instance of the JsonEventSerializer class.
    /// </summary>
    /// <param name="options">Optional custom JSON serializer options</param>
    /// <param name="logger">Optional logger for diagnostic information</param>
    public JsonEventSerializer(
        JsonSerializerOptions? options = null,
        ILogger<JsonEventSerializer>? logger = null)
    {
        _options = options ?? DefaultOptions;
        _logger = logger;
        _eventTypeRegistry = [];

        // Register known event types
        RegisterKnownEventTypes();
    }

    /// <summary>
    /// Serializes an event to JSON string.
    /// </summary>
    /// <param name="eventData">The event to serialize</param>
    /// <returns>JSON representation of the event</returns>
    /// <exception cref="ArgumentNullException">Thrown when event is null</exception>
    /// <exception cref="EventSerializationException">Thrown when serialization fails</exception>
    public string Serialize(IEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger?.LogDebug("Serializing event {EventType} with ID {EventId}", eventData.EventType, eventData.EventId);

            // Create an event envelope that includes the eventType field
            var eventEnvelope = new
            {
                eventType = eventData.EventType,
                eventId = eventData.EventId,
                streamId = eventData.StreamId,
                version = eventData.Version,
                timestamp = eventData.Timestamp,
                correlationId = eventData.CorrelationId,
                causationId = eventData.CausationId,
                metadata = eventData.Metadata,
                data = JsonSerializer.SerializeToElement(eventData, eventData.GetType(), _options)
            };

            var json = JsonSerializer.Serialize(eventEnvelope, _options);

            _logger?.LogDebug("Successfully serialized event {EventId} to {ByteCount} bytes",
                eventData.EventId, System.Text.Encoding.UTF8.GetByteCount(json));

            return json;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to serialize event {EventType} with ID {EventId}", eventData.EventType, eventData.EventId);
            throw new EventSerializationException(
                $"Failed to serialize event of type {eventData.EventType}",
                eventData.EventType,
                eventData.EventId,
                eventData.CorrelationId,
                ex);
        }
    }

    /// <summary>
    /// Deserializes an event from JSON string.
    /// </summary>
    /// <param name="eventType">The type of event to deserialize</param>
    /// <param name="eventData">The JSON event data</param>
    /// <returns>The deserialized event</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventType or eventData is null</exception>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    public IEvent Deserialize(string eventType, string eventData)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger?.LogDebug("Deserializing event type {EventType} from {ByteCount} bytes",
                eventType, System.Text.Encoding.UTF8.GetByteCount(eventData));

            if (!_eventTypeRegistry.TryGetValue(eventType, out var type))
            {
                _logger?.LogWarning("Unknown event type {EventType}, attempting dynamic resolution", eventType);
                type = ResolveEventType(eventType);
            }

            // Parse the event data to check if it's envelope format or direct event data
            using var document = JsonDocument.Parse(eventData);
            var root = document.RootElement;

            IEvent? parsedEvent;
            if (root.TryGetProperty("eventType", out var eventTypeProperty) &&
                root.TryGetProperty("data", out var dataProperty))
            {
                // This is envelope format - deserialize the data part
                var dataJson = dataProperty.GetRawText();
                var deserializedData = JsonSerializer.Deserialize(dataJson, type, _options);
                parsedEvent = deserializedData as IEvent;
            }
            else
            {
                // This is direct event data format (backward compatibility)
                var deserializedData = JsonSerializer.Deserialize(eventData, type, _options);
                parsedEvent = deserializedData as IEvent;
            }

            if (parsedEvent == null)
            {
                throw new EventDeserializationException(
                    $"Deserialization of event type {eventType} returned null",
                    eventType,
                    eventData);
            }

            _logger?.LogDebug("Successfully deserialized event {EventId} of type {EventType}",
                parsedEvent.EventId, eventType);

            return parsedEvent;
        }
        catch (Exception ex) when (ex is not EventDeserializationException)
        {
            _logger?.LogError(ex, "Failed to deserialize event type {EventType}", eventType);
            throw new EventDeserializationException(
                $"Failed to deserialize event of type {eventType}",
                eventType,
                eventData,
                innerException: ex);
        }
    }

    /// <summary>
    /// Deserializes an event to a specific type.
    /// </summary>
    /// <typeparam name="T">The specific event type</typeparam>
    /// <param name="eventData">The JSON event data</param>
    /// <returns>The deserialized event</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventData is null</exception>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    public T Deserialize<T>(string eventData) where T : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            var eventType = typeof(T).Name;
            _logger?.LogDebug("Deserializing event to type {EventType} from {ByteCount} bytes",
                eventType, System.Text.Encoding.UTF8.GetByteCount(eventData));

            // Parse the event envelope
            using var document = JsonDocument.Parse(eventData);
            var root = document.RootElement;

            // Check if this is an envelope format (has eventType field) or direct event data
            T parsedEvent;
            if (root.TryGetProperty("eventType", out var eventTypeProperty) &&
                root.TryGetProperty("data", out var dataProperty))
            {
                // This is envelope format - deserialize the data part
                var dataJson = dataProperty.GetRawText();
                parsedEvent = JsonSerializer.Deserialize<T>(dataJson, _options)!;
            }
            else
            {
                // This is direct event data format (backward compatibility)
                parsedEvent = JsonSerializer.Deserialize<T>(eventData, _options)!;
            }

            if (EqualityComparer<T>.Default.Equals(parsedEvent, default))
            {
                throw new EventDeserializationException(
                    $"Deserialization to type {typeof(T).Name} returned null",
                    eventType,
                    eventData);
            }

            _logger?.LogDebug("Successfully deserialized event {EventId} to type {EventType}",
                (parsedEvent as IEvent)?.EventId, eventType);

            return parsedEvent;
        }
        catch (Exception ex) when (ex is not EventDeserializationException)
        {
            var eventType = typeof(T).Name;
            _logger?.LogError(ex, "Failed to deserialize event to type {EventType}", eventType);
            throw new EventDeserializationException(
                $"Failed to deserialize event to type {eventType}",
                eventType,
                eventData,
                innerException: ex);
        }
    }

    /// <summary>
    /// Serializes event metadata to JSON string.
    /// </summary>
    /// <param name="metadata">The metadata to serialize</param>
    /// <returns>JSON representation of metadata, or null if metadata is null</returns>
    /// <exception cref="EventSerializationException">Thrown when serialization fails</exception>
    public string? SerializeMetadata(Dictionary<string, object>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return null;
        }

        try
        {
            _logger?.LogDebug("Serializing metadata with {Count} entries", metadata.Count);
            return JsonSerializer.Serialize(metadata, _options);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to serialize metadata");
            throw new EventSerializationException(
                "Failed to serialize event metadata",
                innerException: ex);
        }
    }

    /// <summary>
    /// Deserializes event metadata from JSON string.
    /// </summary>
    /// <param name="metadataJson">The JSON metadata</param>
    /// <returns>The deserialized metadata, or null if metadataJson is null</returns>
    /// <exception cref="EventDeserializationException">Thrown when deserialization fails</exception>
    public Dictionary<string, object>? DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrEmpty(metadataJson))
        {
            return null;
        }

        try
        {
            _logger?.LogDebug("Deserializing metadata from {ByteCount} bytes",
                System.Text.Encoding.UTF8.GetByteCount(metadataJson));

            var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _options);

            _logger?.LogDebug("Successfully deserialized metadata with {Count} entries", metadata?.Count ?? 0);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to deserialize metadata");
            throw new EventDeserializationException(
                "Failed to deserialize event metadata",
                eventData: metadataJson,
                innerException: ex);
        }
    }

    /// <summary>
    /// Checks if this serializer supports the given event type.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>True if the event type is supported</returns>
    public bool SupportsEventType(string eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _eventTypeRegistry.ContainsKey(eventType) || CanResolveEventType(eventType);
    }

    /// <summary>
    /// Registers an event type with the serializer.
    /// </summary>
    /// <param name="eventType">The event type name</param>
    /// <param name="type">The .NET type</param>
    public void RegisterEventType(string eventType, Type type)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(IEvent).IsAssignableFrom(type))
        {
            throw new ArgumentException($"Type {type.Name} does not implement IEvent", nameof(type));
        }

        _eventTypeRegistry[eventType] = type;
        _logger?.LogDebug("Registered event type {EventType} -> {TypeName}", eventType, type.Name);
    }

    /// <summary>
    /// Creates the default JSON serializer options optimized for event sourcing.
    /// </summary>
    /// <returns>Configured JSON serializer options</returns>
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false, // Compact JSON for storage efficiency
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // Add custom converters for Orleans compatibility
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DateTimeOffsetConverter());
        options.Converters.Add(new DictionaryStringObjectConverter());

        return options;
    }

    /// <summary>
    /// Registers known event types for efficient serialization.
    /// </summary>
    private void RegisterKnownEventTypes()
    {
        // Register concrete event types
        RegisterEventType(nameof(ChatMessageSentEvent), typeof(ChatMessageSentEvent));
        RegisterEventType(nameof(StateChangedEvent), typeof(StateChangedEvent));

        _logger?.LogDebug("Registered {Count} known event types", _eventTypeRegistry.Count);
    }

    /// <summary>
    /// Attempts to resolve an event type dynamically.
    /// </summary>
    /// <param name="eventType">The event type name</param>
    /// <returns>The resolved .NET type</returns>
    /// <exception cref="EventDeserializationException">Thrown when type cannot be resolved</exception>
    private Type ResolveEventType(string eventType)
    {
        try
        {
            // Try to find the type in the current assembly
            var currentAssembly = typeof(IEvent).Assembly;
            var type = currentAssembly.GetType($"{typeof(IEvent).Namespace}.{eventType}");

            if (type != null && typeof(IEvent).IsAssignableFrom(type))
            {
                _eventTypeRegistry[eventType] = type; // Cache for future use
                _logger?.LogDebug("Dynamically resolved event type {EventType} -> {TypeName}", eventType, type.Name);
                return type;
            }

            // Try other loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == eventType && typeof(IEvent).IsAssignableFrom(t));

                if (type != null)
                {
                    _eventTypeRegistry[eventType] = type; // Cache for future use
                    _logger?.LogDebug("Dynamically resolved event type {EventType} from {AssemblyName}", eventType, assembly.GetName().Name);
                    return type;
                }
            }

            throw new EventDeserializationException(
                $"Could not resolve event type '{eventType}' to a .NET type",
                eventType);
        }
        catch (Exception ex) when (ex is not EventDeserializationException)
        {
            throw new EventDeserializationException(
                $"Error resolving event type '{eventType}'",
                eventType,
                innerException: ex);
        }
    }

    /// <summary>
    /// Checks if an event type can be resolved without actually resolving it.
    /// </summary>
    /// <param name="eventType">The event type name</param>
    /// <returns>True if the type can be resolved</returns>
    private static bool CanResolveEventType(string eventType)
    {
        try
        {
            // Try to find the type in the current assembly
            var currentAssembly = typeof(IEvent).Assembly;
            var type = currentAssembly.GetType($"{typeof(IEvent).Namespace}.{eventType}");

            if (type != null && typeof(IEvent).IsAssignableFrom(type))
            {
                return true;
            }

            // Check other loaded assemblies
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetTypes()
                    .Any(t => t.Name == eventType && typeof(IEvent).IsAssignableFrom(t)));
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Custom JSON converter for DateTimeOffset that ensures consistent serialization.
/// </summary>
public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <summary>
    /// Reads a DateTimeOffset from JSON.
    /// </summary>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && DateTimeOffset.TryParse(reader.GetString(), out var result))
        {
            return result;
        }
        return reader.GetDateTimeOffset();
    }

    /// <summary>
    /// Writes a DateTimeOffset to JSON in ISO 8601 format.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O")); // ISO 8601 format
    }
}

/// <summary>
/// Custom JSON converter for Dictionary&lt;string, object&gt; that handles Orleans-compatible serialization.
/// </summary>
public class DictionaryStringObjectConverter : JsonConverter<Dictionary<string, object>>
{
    /// <summary>
    /// Reads a dictionary from JSON.
    /// </summary>
    public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        var dictionary = new Dictionary<string, object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dictionary;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name");
            }

            var propertyName = reader.GetString()!;
            reader.Read();

            dictionary[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON");
    }

    /// <summary>
    /// Writes a dictionary to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value);
        }

        writer.WriteEndObject();
    }

    private static object ReadValue(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number when reader.TryGetInt32(out var intValue) => intValue,
            JsonTokenType.Number when reader.TryGetInt64(out var longValue) => longValue,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null!,
            JsonTokenType.StartObject => ReadObject(ref reader),
            JsonTokenType.StartArray => ReadArray(ref reader),
            _ => throw new JsonException($"Unsupported token type: {reader.TokenType}")
        };
    }

    private static Dictionary<string, object> ReadObject(ref Utf8JsonReader reader)
    {
        var obj = new Dictionary<string, object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return obj;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name");
            }

            var propertyName = reader.GetString()!;
            reader.Read();
            obj[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON");
    }

    private static List<object> ReadArray(ref Utf8JsonReader reader)
    {
        var array = new List<object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return array;
            }

            array.Add(ReadValue(ref reader));
        }

        throw new JsonException("Unexpected end of JSON");
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case Dictionary<string, object> dictValue:
                writer.WriteStartObject();
                foreach (var kvp in dictValue)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteValue(writer, kvp.Value);
                }
                writer.WriteEndObject();
                break;
            case IEnumerable<object> arrayValue:
                writer.WriteStartArray();
                foreach (var item in arrayValue)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
