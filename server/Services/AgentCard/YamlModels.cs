using YamlDotNet.Serialization;

namespace AIChat.Server.Services.AgentCards
{
    /// <summary>
    /// Strongly-typed model for the YAML front matter in agent cards.
    /// </summary>
    public sealed class AgentYamlMetadata
    {
        /// <summary>
        /// Unique identifier for the agent.
        /// </summary>
        [YamlMember(Alias = "agent")]
        public string Agent { get; set; } = "";

        /// <summary>
        /// Display name of the agent.
        /// </summary>
        [YamlMember(Alias = "name")]
        public string? Name { get; set; }

        /// <summary>
        /// Version of the agent configuration.
        /// </summary>
        [YamlMember(Alias = "version")]
        public string? Version { get; set; }

        /// <summary>
        /// Category for organizing agents.
        /// </summary>
        [YamlMember(Alias = "category")]
        public string? Category { get; set; }

        /// <summary>
        /// Suggested models for this agent.
        /// </summary>
        [YamlMember(Alias = "model_hints")]
        public List<string>? ModelHints { get; set; }

        /// <summary>
        /// Agent capabilities configuration.
        /// </summary>
        [YamlMember(Alias = "capabilities")]
        public AgentYamlCapabilities? Capabilities { get; set; }

        /// <summary>
        /// Output contract type.
        /// </summary>
        [YamlMember(Alias = "output_contract")]
        public string? OutputContract { get; set; }

        /// <summary>
        /// Risk level assessment.
        /// </summary>
        [YamlMember(Alias = "risk_level")]
        public string? RiskLevel { get; set; }

        /// <summary>
        /// Tags for categorization and search.
        /// </summary>
        [YamlMember(Alias = "tags")]
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Agent capabilities configuration in YAML.
    /// </summary>
    public sealed class AgentYamlCapabilities
    {
        /// <summary>
        /// List of tool identifiers available to the agent.
        /// </summary>
        [YamlMember(Alias = "tools")]
        public List<string>? Tools { get; set; }

        /// <summary>
        /// Memory type configuration.
        /// </summary>
        [YamlMember(Alias = "memory")]
        public string? Memory { get; set; }

        /// <summary>
        /// Maximum token limit for responses.
        /// </summary>
        [YamlMember(Alias = "max_tokens")]
        public int? MaxTokens { get; set; }
    }
}
