namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Factory interface for creating StreamingBridge instances.
/// </summary>
public interface IStreamingBridgeFactory
{
    /// <summary>
    /// Creates a new instance of IStreamingBridge.
    /// </summary>
    /// <returns>A new IStreamingBridge instance</returns>
    IStreamingBridge CreateBridge();

    /// <summary>
    /// Creates a new instance of IStreamingBridge with a custom configuration.
    /// </summary>
    /// <param name="configureOptions">Action to configure the streaming options</param>
    /// <returns>A new IStreamingBridge instance with custom configuration</returns>
    IStreamingBridge CreateBridge(Action<Configuration.StreamingConfiguration> configureOptions);
}
