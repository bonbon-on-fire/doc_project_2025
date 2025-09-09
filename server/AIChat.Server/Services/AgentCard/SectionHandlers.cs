using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIChat.Server.Services.AgentCards;

/// <summary>
/// Interface for handling specific sections within an agent card.
/// </summary>
public interface ISectionHandler
{
    /// <summary>
    /// Gets the name of the section this handler processes.
    /// </summary>
    string SectionName { get; }

    /// <summary>
    /// Processes the section content and updates the agent card.
    /// </summary>
    /// <param name="content">The raw content of the section</param>
    /// <param name="card">The agent card to update</param>
    /// <returns>Result indicating success or failure with error message</returns>
    Result ProcessSection(string content, AgentCard card);
}

/// <summary>
/// Base class for section handlers providing common functionality.
/// </summary>
public abstract class BaseSectionHandler : ISectionHandler
{
    public abstract string SectionName { get; }

    public abstract Result ProcessSection(string content, AgentCard card);

    /// <summary>
    /// Validates that content is not empty or whitespace.
    /// </summary>
    protected Result ValidateContent(string content)
    {
        return string.IsNullOrWhiteSpace(content)
            ? Result.Failure($"{SectionName} section cannot be empty")
            : Result.Success();
    }
}

/// <summary>
/// Handles the ROLE section of an agent card.
/// </summary>
public sealed class RoleSectionHandler : BaseSectionHandler
{
    public override string SectionName => "ROLE";

    public override Result ProcessSection(string content, AgentCard card)
    {
        var validation = ValidateContent(content);
        if (validation.IsFailure)
        {
            return validation;
        }

        card.Role = content.Trim();
        return Result.Success();
    }
}

/// <summary>
/// Handles the OBJECTIVE section of an agent card.
/// </summary>
public sealed class ObjectiveSectionHandler : BaseSectionHandler
{
    public override string SectionName => "OBJECTIVE";

    public override Result ProcessSection(string content, AgentCard card)
    {
        var validation = ValidateContent(content);
        if (validation.IsFailure)
        {
            return validation;
        }

        card.Objective = content.Trim();
        return Result.Success();
    }
}

/// <summary>
/// Handles the WORKFLOW section of an agent card.
/// </summary>
public sealed partial class WorkflowSectionHandler : BaseSectionHandler
{
    private static readonly Regex StepPattern = MyRegex();
    private static readonly Regex PhasePattern = MyRegex1();

    public override string SectionName => "WORKFLOW";

    public override Result ProcessSection(string content, AgentCard card)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            card.Workflow = [];
            return Result.Success();
        }

        var steps = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Match numbered steps like "1. Step description" or "## Phase Name"
            if (StepPattern.IsMatch(trimmed) || PhasePattern.IsMatch(trimmed))
            {
                steps.Add(trimmed);
            }
        }

        card.Workflow = steps;
        return Result.Success();
    }

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"^##\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex1();
}

/// <summary>
/// Handles the OUTPUT SCHEMA section of an agent card.
/// </summary>
public sealed partial class OutputSchemaSectionHandler : BaseSectionHandler
{
    private static readonly Regex JsonBlockPattern = MyRegex();

    public override string SectionName => "OUTPUT SCHEMA";

    public override Result ProcessSection(string content, AgentCard card)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            card.OutputSchema = null;
            return Result.Success();
        }

        var jsonMatch = JsonBlockPattern.Match(content);
        if (!jsonMatch.Success)
        {
            // No JSON block found, which is acceptable
            card.OutputSchema = null;
            return Result.Success();
        }

        var json = jsonMatch.Groups[1].Value;
        try
        {
            card.OutputSchema = JsonDocument.Parse(json);
            return Result.Success();
        }
        catch (JsonException)
        {
            // Graceful degradation - log the error but don't fail the entire parsing
            card.OutputSchema = null;
            return Result.Success(); // We still succeed, just with no schema
        }
    }

    [GeneratedRegex(@"```json\s*\n(.*?)\n```", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex MyRegex();
}

/// <summary>
/// Generic handler for sections that are stored as-is without special processing.
/// </summary>
public sealed class GenericSectionHandler(string sectionName) : ISectionHandler
{
    public string SectionName { get; } =
        sectionName ?? throw new ArgumentNullException(nameof(sectionName));

    public Result ProcessSection(string content, AgentCard card)
    {
        // Store the content in the generic sections dictionary
        card.Sections[SectionName] = content?.Trim() ?? "";
        return Result.Success();
    }
}

/// <summary>
/// Registry for managing section handlers.
/// </summary>
public sealed class SectionHandlerRegistry
{
    private readonly Dictionary<string, ISectionHandler> _handlers;
    private readonly HashSet<string> _knownSections;

    public SectionHandlerRegistry()
    {
        _handlers = new Dictionary<string, ISectionHandler>(StringComparer.OrdinalIgnoreCase);
        _knownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Register default handlers
        RegisterDefaultHandlers();
    }

    private void RegisterDefaultHandlers()
    {
        Register(new RoleSectionHandler());
        Register(new ObjectiveSectionHandler());
        Register(new WorkflowSectionHandler());
        Register(new OutputSchemaSectionHandler());

        // Register generic handlers for other common sections
        var genericSections = new[]
        {
            "CONTEXT",
            "TOOLS",
            "CONSTRAINTS",
            "STYLE",
            "EXAMPLES",
            "NOTES",
            "REQUIREMENTS",
            "REFERENCES",
        };

        foreach (var section in genericSections)
        {
            Register(new GenericSectionHandler(section));
            _ = _knownSections.Add(section);
        }
    }

    /// <summary>
    /// Registers a section handler.
    /// </summary>
    public void Register(ISectionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[handler.SectionName] = handler;
        _ = _knownSections.Add(handler.SectionName);
    }

    /// <summary>
    /// Gets a handler for the specified section name.
    /// </summary>
    public ISectionHandler? GetHandler(string sectionName)
    {
        return string.IsNullOrWhiteSpace(sectionName) ? null
            : _handlers.TryGetValue(sectionName, out var handler) ? handler
            : null;
    }

    /// <summary>
    /// Gets or creates a handler for the specified section name.
    /// </summary>
    public ISectionHandler GetOrCreateHandler(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            throw new ArgumentException("Section name cannot be empty", nameof(sectionName));
        }

        // Try to get existing handler
        var handler = GetHandler(sectionName);
        if (handler != null)
        {
            return handler;
        }

        // Create a generic handler for unknown sections
        var genericHandler = new GenericSectionHandler(sectionName);
        Register(genericHandler);
        return genericHandler;
    }

    /// <summary>
    /// Checks if a section name is known/registered.
    /// </summary>
    public bool IsKnownSection(string sectionName)
    {
        return !string.IsNullOrWhiteSpace(sectionName) && _knownSections.Contains(sectionName);
    }
}
