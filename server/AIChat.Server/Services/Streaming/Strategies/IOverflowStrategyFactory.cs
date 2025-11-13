using AIChat.Server.Configuration;

namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Interface for creating overflow strategy instances.
/// </summary>
public interface IOverflowStrategyFactory
{
    /// <summary>
    /// Gets an overflow strategy by type.
    /// </summary>
    /// <param name="strategyType">Type of strategy to get</param>
    /// <returns>The requested strategy instance</returns>
    IOverflowStrategy GetStrategy(OverflowStrategy strategyType);

    /// <summary>
    /// Gets statistics for all strategies.
    /// </summary>
    /// <returns>Dictionary of strategy statistics</returns>
    Dictionary<string, OverflowStrategyStatistics> GetAllStatistics();

    /// <summary>
    /// Registers a custom overflow strategy.
    /// </summary>
    /// <param name="strategyType">Type identifier for the strategy</param>
    /// <param name="strategyFactory">Factory function to create the strategy</param>
    void RegisterStrategy(OverflowStrategy strategyType, Func<IServiceProvider, IOverflowStrategy> strategyFactory);
}
