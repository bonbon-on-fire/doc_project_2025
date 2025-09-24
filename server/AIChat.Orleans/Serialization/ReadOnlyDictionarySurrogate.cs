using AIChat.Orleans.Contracts;
using Orleans;

namespace AIChat.Orleans.Serialization;

/// <summary>
/// Surrogate type for IReadOnlyDictionary&lt;string, UserSessionState&gt; Orleans serialization.
/// Converts the interface to a concrete Dictionary for serialization purposes.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Serialization.ReadOnlyDictionaryUserSessionStateSurrogate")]
public struct ReadOnlyDictionaryUserSessionStateSurrogate
{
    /// <summary>
    /// The underlying dictionary data.
    /// </summary>
    [Id(0)]
    public Dictionary<string, UserSessionState> Data { get; set; }
}

/// <summary>
/// Converter for IReadOnlyDictionary&lt;string, UserSessionState&gt; surrogate serialization.
/// </summary>
[RegisterConverter]
public sealed class ReadOnlyDictionaryUserSessionStateConverter :
    IConverter<IReadOnlyDictionary<string, UserSessionState>, ReadOnlyDictionaryUserSessionStateSurrogate>
{
    /// <summary>
    /// Converts IReadOnlyDictionary to surrogate for serialization.
    /// </summary>
    /// <param name="value">The IReadOnlyDictionary to convert</param>
    /// <returns>The surrogate representation</returns>
    public ReadOnlyDictionaryUserSessionStateSurrogate ConvertToSurrogate(in IReadOnlyDictionary<string, UserSessionState> value)
    {
        return new ReadOnlyDictionaryUserSessionStateSurrogate
        {
            Data = value?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? []
        };
    }

    /// <summary>
    /// Converts surrogate back to IReadOnlyDictionary after deserialization.
    /// </summary>
    /// <param name="surrogate">The surrogate to convert</param>
    /// <returns>The IReadOnlyDictionary representation</returns>
    public IReadOnlyDictionary<string, UserSessionState> ConvertFromSurrogate(in ReadOnlyDictionaryUserSessionStateSurrogate surrogate)
    {
        return surrogate.Data ?? [];
    }
}