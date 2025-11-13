using AIChat.Server.Models;

namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Concrete validator implementation for Chat entities.
/// Demonstrates real validation logic including business rules and data format validation.
/// </summary>
public sealed class ChatValidator : StateValidatorBase<Chat>
{
    /// <summary>
    /// Gets the name of this validator.
    /// </summary>
    public override string Name => "ChatValidator";

    /// <summary>
    /// Initializes a new instance of the ChatValidator class.
    /// </summary>
    /// <param name="logger">Logger for validation operations</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    public ChatValidator(
        ILogger<ChatValidator> logger,
        IServiceProvider serviceProvider)
        : base(logger, serviceProvider)
    {
    }

    /// <inheritdoc />
    protected override Task<StateValidationResult> ValidateCustomAsync(Chat entity, ValidationOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Validate ID format (should be a valid GUID)
        if (!string.IsNullOrEmpty(entity.Id) && !Guid.TryParse(entity.Id, out _))
        {
            errors.Add(ValidationError.Create(
                nameof(entity.Id),
                "Chat ID must be a valid GUID format",
                ValidationErrorCode.InvalidFormat,
                entity.Id));
        }

        // Validate UserId format
        if (!string.IsNullOrEmpty(entity.UserId) && !Guid.TryParse(entity.UserId, out _))
        {
            errors.Add(ValidationError.Create(
                nameof(entity.UserId),
                "User ID must be a valid GUID format",
                ValidationErrorCode.InvalidFormat,
                entity.UserId));
        }

        // Basic UserId validation (format check only - referential integrity would require additional storage dependencies)
        if (!string.IsNullOrEmpty(entity.UserId))
        {
            // For demonstration purposes, we'll just warn about referential integrity
            // In a real implementation, you would check against user storage here
            warnings.Add(ValidationWarning.Create(
                nameof(entity.UserId),
                "Referential integrity check for user existence is not implemented in this demo validator",
                entity.UserId));
        }

        // Validate title content
        if (!string.IsNullOrEmpty(entity.Title))
        {
            // Check for potentially inappropriate content (basic example)
            if (entity.Title.Contains("<script>", StringComparison.OrdinalIgnoreCase) ||
                entity.Title.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(ValidationError.Create(
                    nameof(entity.Title),
                    "Chat title contains potentially unsafe content",
                    ValidationErrorCode.BusinessRuleViolation,
                    entity.Title));
            }

            // Warn if title is very short
            if (entity.Title.Trim().Length < 3)
            {
                warnings.Add(ValidationWarning.Create(
                    nameof(entity.Title),
                    "Chat title is very short and may not be descriptive",
                    entity.Title));
            }
        }

        // Validate timestamps
        if (entity.CreatedAt > DateTime.UtcNow.AddMinutes(5)) // Allow some clock skew
        {
            errors.Add(ValidationError.Create(
                nameof(entity.CreatedAt),
                "Created date cannot be in the future",
                ValidationErrorCode.OutOfRange,
                entity.CreatedAt));
        }

        if (entity.UpdatedAt < entity.CreatedAt)
        {
            errors.Add(ValidationError.Create(
                nameof(entity.UpdatedAt),
                "Updated date cannot be before created date",
                ValidationErrorCode.BusinessRuleViolation,
                entity.UpdatedAt));
        }

        return Task.FromResult(errors.Count > 0
            ? StateValidationResult.Failed(errors, warnings.Count > 0 ? warnings : null)
            : StateValidationResult.Success(warnings.Count > 0 ? warnings : null));
    }

    /// <inheritdoc />
    protected override async Task<StateValidationResult> ValidateCustomUpdateAsync(string entityId, Chat entity, Chat? existingEntity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(entity);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Call base custom validation first
        var baseResult = await ValidateCustomAsync(entity, ValidationOperation.Update, cancellationToken).ConfigureAwait(false);
        errors.AddRange(baseResult.Errors);
        warnings.AddRange(baseResult.Warnings);

        // Additional update-specific validation
        if (existingEntity != null)
        {
            // Prevent changing UserId after creation
            if (entity.UserId != existingEntity.UserId)
            {
                errors.Add(ValidationError.Create(
                    nameof(entity.UserId),
                    "User ID cannot be changed after chat creation",
                    ValidationErrorCode.BusinessRuleViolation,
                    entity.UserId));
            }

            // Prevent changing CreatedAt
            if (entity.CreatedAt != existingEntity.CreatedAt)
            {
                errors.Add(ValidationError.Create(
                    nameof(entity.CreatedAt),
                    "Created date cannot be modified",
                    ValidationErrorCode.BusinessRuleViolation,
                    entity.CreatedAt));
            }

            // Ensure UpdatedAt is being updated
            if (entity.UpdatedAt <= existingEntity.UpdatedAt)
            {
                warnings.Add(ValidationWarning.Create(
                    nameof(entity.UpdatedAt),
                    "Update timestamp should be more recent than existing timestamp",
                    entity.UpdatedAt));
            }
        }

        // Validate ID consistency
        if (entity.Id != entityId)
        {
            errors.Add(ValidationError.Create(
                nameof(entity.Id),
                "Entity ID does not match the provided entity ID",
                ValidationErrorCode.BusinessRuleViolation,
                entity.Id));
        }

        return errors.Count > 0
            ? StateValidationResult.Failed(errors, warnings.Count > 0 ? warnings : null)
            : StateValidationResult.Success(warnings.Count > 0 ? warnings : null);
    }

    /// <inheritdoc />
    protected override async Task<StateValidationResult> ValidateCustomDeleteAsync(string entityId, Chat? existingEntity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Check if chat has messages (business rule: don't delete chats with messages)
        if (existingEntity?.Messages != null && existingEntity.Messages.Count > 0)
        {
            warnings.Add(ValidationWarning.Create(
                "Messages",
                $"Chat has {existingEntity.Messages.Count} messages that will be deleted",
                existingEntity.Messages.Count));

            // If there are many messages, consider it an error
            if (existingEntity.Messages.Count > 10)
            {
                errors.Add(ValidationError.Create(
                    "Messages",
                    $"Cannot delete chat with {existingEntity.Messages.Count} messages. Archive the chat instead.",
                    ValidationErrorCode.BusinessRuleViolation,
                    existingEntity.Messages.Count));
            }
        }

        return await Task.FromResult(errors.Count > 0
            ? StateValidationResult.Failed(errors, warnings.Count > 0 ? warnings : null)
            : StateValidationResult.Success(warnings.Count > 0 ? warnings : null));
    }

    /// <inheritdoc />
    protected override IEnumerable<ValidationRuleInfo> GetCustomValidationRules(ValidationOperation operation)
    {
        var rules = new List<ValidationRuleInfo>
        {
            // Common rules for all operations
            ValidationRuleInfo.Create(
            "ChatId_GuidFormat",
            "Chat ID GUID Format",
            "Chat ID must be a valid GUID format",
            nameof(Chat.Id),
            ValidationErrorCode.InvalidFormat),
            ValidationRuleInfo.Create(
            "UserId_GuidFormat",
            "User ID GUID Format",
            "User ID must be a valid GUID format",
            nameof(Chat.UserId),
            ValidationErrorCode.InvalidFormat),
            ValidationRuleInfo.Create(
            "UserId_ReferentialIntegrity",
            "User Existence Check",
            "Referenced user must exist in the system",
            nameof(Chat.UserId),
            ValidationErrorCode.ReferentialIntegrity),
            ValidationRuleInfo.Create(
            "Title_SafeContent",
            "Safe Title Content",
            "Chat title must not contain unsafe content",
            nameof(Chat.Title),
            ValidationErrorCode.BusinessRuleViolation),
            ValidationRuleInfo.Create(
            "CreatedAt_NotFuture",
            "Created Date Validation",
            "Created date cannot be in the future",
            nameof(Chat.CreatedAt),
            ValidationErrorCode.OutOfRange),
            ValidationRuleInfo.Create(
            "UpdatedAt_AfterCreated",
            "Update Date Validation",
            "Updated date must be after created date",
            nameof(Chat.UpdatedAt),
            ValidationErrorCode.BusinessRuleViolation)
        };

        // Operation-specific rules
        if (operation == ValidationOperation.Update)
        {
            rules.Add(ValidationRuleInfo.Create(
                "UserId_Immutable",
                "User ID Immutability",
                "User ID cannot be changed after creation",
                nameof(Chat.UserId),
                ValidationErrorCode.BusinessRuleViolation));

            rules.Add(ValidationRuleInfo.Create(
                "CreatedAt_Immutable",
                "Created Date Immutability",
                "Created date cannot be modified",
                nameof(Chat.CreatedAt),
                ValidationErrorCode.BusinessRuleViolation));
        }

        if (operation == ValidationOperation.Delete)
        {
            rules.Add(ValidationRuleInfo.Create(
                "Messages_DeleteRestriction",
                "Message Count Delete Restriction",
                "Chats with many messages should be archived instead of deleted",
                "Messages",
                ValidationErrorCode.BusinessRuleViolation));
        }

        return rules;
    }
}
