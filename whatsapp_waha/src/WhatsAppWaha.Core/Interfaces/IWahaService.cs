using System.Threading;
using System.Threading.Tasks;
using WhatsAppWaha.Core.Models;

namespace WhatsAppWaha.Core.Interfaces;

/// <summary>
/// Contract for interacting with the WAHA (WhatsApp HTTP API) service.
/// </summary>
public interface IWahaService
{
  /// <summary>
  /// Validates the format of the phone number. International numbers expected (E.164 or WAHA accepted formats).
  /// </summary>
  /// <param name="phoneNumber">The phone number to validate.</param>
  /// <returns>True if the phone number format is valid; otherwise false.</returns>
  bool ValidatePhoneNumber(string phoneNumber);

  /// <summary>
  /// Sends a text message to the specified phone number via WAHA.
  /// </summary>
  /// <param name="phoneNumber">Recipient phone number (e.g., +15551234567).</param>
  /// <param name="message">The text message content.</param>
  /// <param name="options">Optional parameters for advanced messaging features.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A result describing the outcome including message id and status.</returns>
  Task<WahaMessageResult> SendTextMessageAsync(string phoneNumber, string message, SendTextOptions? options = null, CancellationToken cancellationToken = default);

  /// <summary>
  /// Validates that the configured WAHA session is authenticated and ready.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>True if the session is valid/authenticated; otherwise false.</returns>
  Task<bool> ValidateSessionAsync(CancellationToken cancellationToken = default);
}
