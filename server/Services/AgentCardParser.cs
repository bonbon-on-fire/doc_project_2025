using System.Text.Json;
using System.Text.RegularExpressions;
using AIChat.Server.Services.AgentCards;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AIChat.Server.Services
{
    /// <summary>
    /// Parser for Agent Card files (.agent.md) that contain YAML front matter and markdown sections.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the AgentCardParser class.
    /// </remarks>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public partial class AgentCardParser(ILogger<AgentCardParser>? logger = null)
    {
        // Compiled regex patterns for better performance
        private static readonly Regex FrontMatterRegex = MyRegex();

        private static readonly Regex SectionRegex = MyRegex1();

        private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        private readonly SectionHandlerRegistry _sectionRegistry = new SectionHandlerRegistry();

        /// <summary>
        /// Parses an agent card from the provided content string.
        /// </summary>
        /// <param name="content">The agent card content containing YAML front matter and markdown</param>
        /// <returns>Result containing the parsed agent card or error message</returns>
        public Result<AgentCard> ParseAgentCard(string content)
        {
            // Validate input
            var validationResult = ValidateContent(content);
            if (validationResult.IsFailure)
            {
                return Result<AgentCard>.Failure(validationResult.Error!);
            }

            // Extract and parse YAML front matter
            var frontMatterResult = ExtractFrontMatter(content);
            if (frontMatterResult.IsFailure)
            {
                return Result<AgentCard>.Failure(frontMatterResult.Error!);
            }

            var (yamlContent, markdownContent) = frontMatterResult.Value;

            // Parse YAML metadata
            var metadataResult = ParseYamlMetadata(yamlContent);
            if (metadataResult.IsFailure)
            {
                return Result<AgentCard>.Failure(metadataResult.Error!);
            }

            // Create agent card with metadata
            var card = new AgentCard
            {
                Metadata = ConvertToAgentMetadata(metadataResult.Value),
                Sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            // Parse markdown sections
            var sectionsResult = ParseSections(markdownContent, card);
            if (sectionsResult.IsFailure)
            {
                // Log warning but continue with partial data (graceful degradation)
                logger?.LogWarning("Failed to parse some sections: {Error}", sectionsResult.Error);
            }

            return Result<AgentCard>.Success(card);
        }

        /// <summary>
        /// Validates the input content.
        /// </summary>
        private static Result ValidateContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure("Agent card content cannot be empty");
            }

            return !FrontMatterRegex.IsMatch(content) ? Result.Failure("Agent card must have YAML front matter") : Result.Success();
        }

        /// <summary>
        /// Extracts the YAML front matter and markdown content from the agent card.
        /// </summary>
        private static Result<(string yamlContent, string markdownContent)> ExtractFrontMatter(
            string content
        )
        {
            var match = FrontMatterRegex.Match(content);
            if (!match.Success)
            {
                return Result<(string, string)>.Failure("Could not extract YAML front matter");
            }

            var yamlContent = match.Groups[1].Value;
            var markdownContent = content.Substring(match.Length);

            return Result<(string, string)>.Success((yamlContent, markdownContent));
        }

        /// <summary>
        /// Parses the YAML metadata using strongly-typed models.
        /// </summary>
        private Result<AgentYamlMetadata> ParseYamlMetadata(string yaml)
        {
            try
            {
                var metadata = _yamlDeserializer.Deserialize<AgentYamlMetadata>(yaml);

                if (metadata == null)
                {
                    return Result<AgentYamlMetadata>.Failure("Failed to deserialize YAML metadata");
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(metadata.Agent))
                {
                    return Result<AgentYamlMetadata>.Failure("Agent ID is required in metadata");
                }

                // Apply defaults
                metadata.Name ??= metadata.Agent;
                metadata.Version ??= "1.0.0";
                metadata.Category ??= "general";

                return Result<AgentYamlMetadata>.Success(metadata);
            }
            catch (YamlException ex)
            {
                logger?.LogError(ex, "Failed to parse YAML metadata");
                return Result<AgentYamlMetadata>.Failure($"Invalid YAML format: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error parsing YAML metadata");
                return Result<AgentYamlMetadata>.Failure($"Failed to parse metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts strongly-typed YAML metadata to AgentMetadata.
        /// </summary>
        private static AgentMetadata ConvertToAgentMetadata(AgentYamlMetadata yamlMetadata)
        {
            return new AgentMetadata
            {
                Agent = yamlMetadata.Agent,
                Name = yamlMetadata.Name ?? yamlMetadata.Agent,
                Version = yamlMetadata.Version ?? "1.0.0",
                Category = yamlMetadata.Category ?? "general",
                ModelHints = yamlMetadata.ModelHints,
                Capabilities =
                    yamlMetadata.Capabilities != null
                        ? new AgentCapabilities
                        {
                            Tools = yamlMetadata.Capabilities.Tools ?? new List<string>(),
                            Memory = yamlMetadata.Capabilities.Memory,
                            MaxTokens = yamlMetadata.Capabilities.MaxTokens,
                        }
                        : null,
                OutputContract = yamlMetadata.OutputContract,
                RiskLevel = yamlMetadata.RiskLevel,
                Tags = yamlMetadata.Tags,
            };
        }

        /// <summary>
        /// Parses markdown sections using the section handler registry.
        /// </summary>
        private Result ParseSections(string markdown, AgentCard card)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return Result.Success();
            }

            var matches = SectionRegex.Matches(markdown);
            var errors = new List<string>();

            foreach (Match match in matches)
            {
                var sectionName = match.Groups[1].Value.Trim();
                var sectionContent = match.Groups[2].Value.Trim();

                // Get or create handler for this section
                var handler = _sectionRegistry.GetOrCreateHandler(sectionName);

                // Process the section
                var result = handler.ProcessSection(sectionContent, card);
                if (result.IsFailure)
                {
                    errors.Add($"{sectionName}: {result.Error}");
                    logger?.LogWarning(
                        "Failed to process section {Section}: {Error}",
                        sectionName,
                        result.Error
                    );
                }

                // Always store the raw content for reference
                if (!card.Sections.ContainsKey(sectionName))
                {
                    card.Sections[sectionName] = sectionContent;
                }
            }

            // Return success even if some sections failed (graceful degradation)
            if (errors.Count != 0)
            {
                logger?.LogWarning(
                    "Some sections had processing errors: {Errors}",
                    string.Join("; ", errors)
                );
            }

            return Result.Success();
        }

        /// <summary>
        /// Loads an agent card from a file.
        /// </summary>
        /// <param name="filePath">Path to the agent card file</param>
        /// <returns>Result containing the parsed agent card or error message</returns>
        public static Result<AgentCard> LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Result<AgentCard>.Failure("File path cannot be empty");
            }

            if (!File.Exists(filePath))
            {
                return Result<AgentCard>.Failure($"Agent card file not found: {filePath}");
            }

            try
            {
                var content = File.ReadAllText(filePath);
                var parser = new AgentCardParser();
                return parser.ParseAgentCard(content);
            }
            catch (IOException ex)
            {
                return Result<AgentCard>.Failure($"Failed to read file: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result<AgentCard>.Failure($"Access denied to file: {ex.Message}");
            }
        }

        [GeneratedRegex(@"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex MyRegex();
        [GeneratedRegex(@"^#\s+([A-Z\s]+)\s*\n(.*?)(?=^#\s+|\z)", RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex MyRegex1();
    }

    /// <summary>
    /// Represents a parsed agent card with metadata and content sections.
    /// </summary>
    public class AgentCard
    {
        /// <summary>
        /// Gets or sets the agent metadata from YAML front matter.
        /// </summary>
        public AgentMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Gets or sets the raw content of all sections.
        /// </summary>
        public Dictionary<string, string> Sections { get; set; } = new();

        /// <summary>
        /// Gets or sets the parsed ROLE section content.
        /// </summary>
        public string? Role { get; set; }

        /// <summary>
        /// Gets or sets the parsed OBJECTIVE section content.
        /// </summary>
        public string? Objective { get; set; }

        /// <summary>
        /// Gets or sets the parsed WORKFLOW steps.
        /// </summary>
        public List<string>? Workflow { get; set; }

        /// <summary>
        /// Gets or sets the parsed OUTPUT SCHEMA as a JSON document.
        /// </summary>
        public JsonDocument? OutputSchema { get; set; }

        private static readonly string[] sourceArray = new[] { "ROLE", "OBJECTIVE", "OUTPUT SCHEMA" };

        /// <summary>
        /// Converts the agent card to a ModeDto object for compatibility with the mode system.
        /// </summary>
        public ModeDto ToMode()
        {
            var prompt = BuildPromptFromSections();

            return new ModeDto
            {
                Id = Metadata.Agent,
                Name = Metadata.Name,
                Description = Objective ?? "Agent mode",
                Category = Metadata.Category,
                Prompt = prompt,
                Tools = Metadata.Capabilities?.Tools ?? new List<string>(),
                DefaultModel = Metadata.ModelHints?.FirstOrDefault(),
                IsSystem = false,
                UserId = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// Builds a complete prompt from all relevant sections.
        /// </summary>
        private string BuildPromptFromSections()
        {
            var promptParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Role))
            {
                promptParts.Add(Role);
            }

            if (!string.IsNullOrWhiteSpace(Objective))
            {
                promptParts.Add($"Objective: {Objective}");
            }

            // Add other relevant sections
            foreach (var section in Sections)
            {
                // Skip sections already added or schema sections
                if (
                    !sourceArray.Contains(
                        section.Key,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                {
                    promptParts.Add($"{section.Key}:\n{section.Value}");
                }
            }

            return string.Join("\n\n", promptParts);
        }
    }

    /// <summary>
    /// Agent metadata extracted from YAML front matter.
    /// </summary>
    public class AgentMetadata
    {
        public string Agent { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Category { get; set; } = "general";
        public List<string>? ModelHints { get; set; }
        public AgentCapabilities? Capabilities { get; set; }
        public string? OutputContract { get; set; }
        public string? RiskLevel { get; set; }
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Agent capabilities configuration.
    /// </summary>
    public class AgentCapabilities
    {
        public List<string> Tools { get; set; } = new();
        public string? Memory { get; set; }
        public int? MaxTokens { get; set; }
    }
}
