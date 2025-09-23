using AIChat.Orleans.Models;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for user preferences management (Phase 2 - ORL-ST-P2-004).
/// Handles persistent storage and retrieval of user-specific preferences including message expansion,
/// mode selection, and UI settings. Migrated from client-side storage to Orleans grain state.
/// This interface follows the Interface Segregation Principle by focusing solely on preference operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserPreferencesGrain")]
public interface IUserPreferencesGrain : IGrainWithStringKey
{
    #region Core Preference Operations

    /// <summary>
    /// Retrieves the complete user preferences state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task with the user's complete preferences state</returns>
    /// <exception cref="InvalidOperationException">Thrown when preferences cannot be retrieved</exception>
    [Alias("GetPreferences")]
    Task<StateResult<UserPreferencesState>> GetPreferencesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the complete user preferences state with optimistic concurrency control.
    /// </summary>
    /// <param name="preferences">The new preferences state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when preferences is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when version conflict occurs</exception>
    [Alias("UpdatePreferences")]
    Task<StateResult> UpdatePreferencesAsync(
        UserPreferencesState preferences,
        CancellationToken cancellationToken = default);

    #endregion

    #region Message Preference Operations

    /// <summary>
    /// Updates the expansion preference for a specific message.
    /// </summary>
    /// <param name="messageId">The message identifier</param>
    /// <param name="isExpanded">Whether the message should be expanded</param>
    /// <param name="renderPhase">Current render phase of the message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    /// <exception cref="ArgumentException">Thrown when renderPhase is invalid</exception>
    [Alias("UpdateMessagePreference")]
    Task<StateResult> UpdateMessagePreferenceAsync(
        string messageId,
        bool isExpanded,
        string renderPhase = "initial",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the preference for a specific message.
    /// </summary>
    /// <param name="messageId">The message identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task with the message preference or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when messageId is null or empty</exception>
    [Alias("GetMessagePreference")]
    Task<StateResult<MessagePreference?>> GetMessagePreferenceAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple message preferences in a single operation for performance.
    /// </summary>
    /// <param name="preferences">Dictionary of message ID to preference mappings</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when preferences is null</exception>
    [Alias("BulkUpdateMessagePreferences")]
    Task<StateResult> BulkUpdateMessagePreferencesAsync(
        Dictionary<string, MessagePreference> preferences,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives old message preferences to prevent storage bloat.
    /// Removes preferences for messages older than the specified date.
    /// </summary>
    /// <param name="olderThan">Archive preferences older than this date</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with number of archived preferences</returns>
    [Alias("ArchiveOldMessagePreferences")]
    Task<StateResult<int>> ArchiveOldMessagePreferencesAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default);

    #endregion

    #region Mode Preference Operations

    /// <summary>
    /// Updates the user's selected mode preference.
    /// </summary>
    /// <param name="modeId">The mode identifier (null to clear selection)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    [Alias("UpdateSelectedMode")]
    Task<StateResult> UpdateSelectedModeAsync(
        string? modeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the user's currently selected mode.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task with the selected mode ID or null if none selected</returns>
    [Alias("GetSelectedMode")]
    Task<StateResult<string?>> GetSelectedModeAsync(
        CancellationToken cancellationToken = default);

    #endregion

    #region UI Preference Operations

    /// <summary>
    /// Updates a specific UI preference setting.
    /// </summary>
    /// <param name="key">The preference key (e.g., "theme", "layout")</param>
    /// <param name="value">The preference value</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    [Alias("UpdateUIPreference")]
    Task<StateResult> UpdateUIPreferenceAsync(
        string key,
        object value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific UI preference setting with type safety.
    /// </summary>
    /// <typeparam name="T">The expected type of the preference value</typeparam>
    /// <param name="key">The preference key</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task with the typed preference value or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    /// <exception cref="InvalidCastException">Thrown when preference cannot be cast to T</exception>
    [Alias("GetUIPreference")]
    Task<StateResult<T?>> GetUIPreferenceAsync<T>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific UI preference setting.
    /// </summary>
    /// <param name="key">The preference key to remove</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with removal result</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    [Alias("RemoveUIPreference")]
    Task<StateResult> RemoveUIPreferenceAsync(
        string key,
        CancellationToken cancellationToken = default);

    #endregion

    #region Migration and Maintenance Operations

    /// <summary>
    /// Imports user preferences from client-side storage during migration.
    /// Merges client data with existing preferences using conflict resolution.
    /// </summary>
    /// <param name="import">The client preferences to import</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with import result</returns>
    /// <exception cref="ArgumentNullException">Thrown when import is null</exception>
    [Alias("ImportClientPreferences")]
    Task<StateResult> ImportClientPreferencesAsync(
        ClientPreferencesImport import,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the complete user preferences for backup or migration purposes.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task with the complete exportable preferences state</returns>
    [Alias("ExportPreferences")]
    Task<StateResult<UserPreferencesState>> ExportPreferencesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all user preferences to default values.
    /// This operation cannot be undone.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with reset result</returns>
    [Alias("ResetPreferences")]
    Task<StateResult> ResetPreferencesAsync(
        CancellationToken cancellationToken = default);

    #endregion

    #region Cache Management Operations

    /// <summary>
    /// Invalidates all preference caches for this user to ensure fresh data.
    /// Should be called after external preference modifications.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with invalidation result</returns>
    [Alias("InvalidatePreferencesCache")]
    Task<StateResult> InvalidatePreferencesCacheAsync(
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Data transfer object for importing client-side preferences to Orleans grain state.
/// Used during migration from client storage to server-side persistent preferences.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ClientPreferencesImport")]
public sealed class ClientPreferencesImport
{
    /// <summary>
    /// Message expansion preferences from client-side storage.
    /// </summary>
    [Id(0)]
    public Dictionary<string, MessagePreference> MessagePreferences { get; set; } = [];

    /// <summary>
    /// Selected mode ID from client-side storage.
    /// </summary>
    [Id(1)]
    public string? SelectedModeId { get; set; }

    /// <summary>
    /// UI preferences from client-side storage.
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> UIPreferences { get; set; } = [];

    /// <summary>
    /// Source identifier for the import (e.g., "client", "localStorage").
    /// </summary>
    [Id(3)]
    public string ImportSource { get; set; } = "client";

    /// <summary>
    /// Timestamp when the import was created.
    /// </summary>
    [Id(4)]
    public DateTime ImportTimestamp { get; set; } = DateTime.UtcNow;
}