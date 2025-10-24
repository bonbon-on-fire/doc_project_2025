using System.Globalization;
using AIChat.Server.Services;
using AIChat.Server.Services.AgentCards;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class AgentCardParserTests
{
    private readonly AgentCardParser _parser;
    private readonly Mock<ILogger<AgentCardParser>> _loggerMock;
    private static readonly string[] expectation = ["gpt-4", "claude-3"];
    private static readonly string[] expectationArray = ["search", "edit"];
    private static readonly string[] expectationArray0 = ["test", "example"];
    private static readonly string[] expectationArray1 = ["tool1", "tool2"];

    public AgentCardParserTests()
    {
        _loggerMock = new Mock<ILogger<AgentCardParser>>();
        _parser = new AgentCardParser(_loggerMock.Object);
    }

    [Fact]
    public void ParseAgentCardWithValidCompleteCardShouldParseAllFields()
    {
        // Arrange
        const string content =
            """
---
agent: "test-agent"
name: "Test Agent"
version: "1.0.0"
category: "testing"
model_hints:
  - "gpt-4"
  - "claude-3"
capabilities:
  tools:
    - "search"
    - "edit"
  memory: "episodic"
  max_tokens: 4096
output_contract: "json_schema"
risk_level: "low"
tags:
  - "test"
  - "example"
---

# ROLE
You are a test agent for validation.

# OBJECTIVE
Validate the parser functionality.

# WORKFLOW
1. First step
2. Second step

# OUTPUT SCHEMA
```json
{
  "name": "TestSchema",
  "schema": {
    "type": "object",
    "properties": {
      "test": { "type": "string" }
    }
  }
}
```
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Error.Should().BeNull();

        var card = result.Value;
        _ = card.Should().NotBeNull();
        _ = card.Metadata.Agent.Should().Be("test-agent");
        _ = card.Metadata.Name.Should().Be("Test Agent");
        _ = card.Metadata.Version.Should().Be("1.0.0");
        _ = card.Metadata.Category.Should().Be("testing");
        _ = card.Metadata.ModelHints.Should().BeEquivalentTo(expectation);
        _ = card.Metadata.Capabilities.Tools.Should().BeEquivalentTo(expectationArray);
        _ = card.Metadata.Capabilities.Memory.Should().Be("episodic");
        _ = card.Metadata.Capabilities.MaxTokens.Should().Be(4096);
        _ = card.Metadata.OutputContract.Should().Be("json_schema");
        _ = card.Metadata.RiskLevel.Should().Be("low");
        _ = card.Metadata.Tags.Should().BeEquivalentTo(expectationArray0);

        _ = card.Role.Should().Be("You are a test agent for validation.");
        _ = card.Objective.Should().Be("Validate the parser functionality.");
        _ = card.Workflow.Should().HaveCount(2);
        _ = card.OutputSchema.Should().NotBeNull();
    }

    [Fact]
    public void ParseAgentCardWithMinimalCardShouldParseRequiredFields()
    {
        // Arrange
        const string content =
            """
---
agent: "minimal-agent"
name: "Minimal Agent"
---

# ROLE
A minimal test agent.

# OBJECTIVE
Test minimal configuration.
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();

        var card = result.Value;
        _ = card.Should().NotBeNull();
        _ = card.Metadata.Agent.Should().Be("minimal-agent");
        _ = card.Metadata.Name.Should().Be("Minimal Agent");
        _ = card.Metadata.Version.Should().Be("1.0.0"); // Default
        _ = card.Metadata.Category.Should().Be("general"); // Default
        _ = card.Role.Should().Be("A minimal test agent.");
        _ = card.Objective.Should().Be("Test minimal configuration.");
    }

    [Fact]
    public void ParseAgentCardWithoutFrontMatterShouldReturnFailure()
    {
        // Arrange
        const string content =
            @"# ROLE
Some role without front matter";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Be("Agent card must have YAML front matter");
    }

    [Fact]
    public void ParseAgentCardWithEmptyContentShouldReturnFailure()
    {
        // Act
        var result = _parser.ParseAgentCard("");

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Be("Agent card content cannot be empty");
    }

    [Fact]
    public void ParseAgentCardWithNullContentShouldReturnFailure()
    {
        // Act
        var result = _parser.ParseAgentCard(null!);

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Be("Agent card content cannot be empty");
    }

    [Fact]
    public void ToModeShouldConvertCorrectly()
    {
        // Arrange
        const string content =
            """
---
agent: "converter-test"
name: "Converter Test"
category: "development"
capabilities:
  tools:
    - "tool1"
    - "tool2"
model_hints:
  - "gpt-4"
---

# ROLE
Test conversion agent.

# OBJECTIVE
Convert to Mode object.

# CONSTRAINTS
- Constraint 1
- Constraint 2
""";

        // Act
        var result = _parser.ParseAgentCard(content);
        var card = result.Value;
        var mode = card.ToMode();

        // Assert
        _ = mode.Should().NotBeNull();
        _ = mode.Id.Should().Be("converter-test");
        _ = mode.Name.Should().Be("Converter Test");
        _ = mode.Description.Should().Be("Convert to Mode object.");
        _ = mode.Category.Should().Be("development");
        _ = mode.Tools.Should().BeEquivalentTo(expectationArray1);
        _ = mode.DefaultModel.Should().Be("gpt-4");
        _ = mode.IsSystem.Should().BeFalse();
        _ = mode.Prompt.Should().Contain("Test conversion agent");
        _ = mode.Prompt.Should().Contain("Objective: Convert to Mode object");
        _ = mode.Prompt.Should().Contain("CONSTRAINTS");
    }

    [Fact]
    public void ParseWorkflowWithNumberedStepsShouldExtractSteps()
    {
        // Arrange
        const string content =
            """
---
agent: "workflow-test"
---

# ROLE
Test agent

# WORKFLOW
1. First step in process
2. Second step with details
3. Third and final step
Some additional text that's not a step
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();
        var card = result.Value;
        _ = card.Workflow.Should().HaveCount(3);
        _ = card.Workflow[0].Should().Be("1. First step in process");
        _ = card.Workflow[1].Should().Be("2. Second step with details");
        _ = card.Workflow[2].Should().Be("3. Third and final step");
    }

    [Fact]
    public void ParseJsonSchemaWithValidSchemaShouldParseCorrectly()
    {
        // Arrange
        const string content =
            """
---
agent: "schema-test"
---

# ROLE
Test

# OUTPUT SCHEMA
```json
{
  "name": "TestOutput",
  "schema": {
    "type": "object",
    "required": ["field1"],
    "properties": {
      "field1": {
        "type": "string",
        "description": "Test field"
      }
    }
  }
}
```
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();
        var card = result.Value;
        _ = card.OutputSchema.Should().NotBeNull();
        var root = card.OutputSchema.RootElement;
        _ = root.GetProperty("name").GetString().Should().Be("TestOutput");
        _ = root.GetProperty("schema").GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void ParseJsonSchemaWithInvalidJsonShouldGracefullyDegrade()
    {
        // Arrange
        const string content =
            """
---
agent: "invalid-schema-test"
---

# ROLE
Test

# OUTPUT SCHEMA
```json
{
  "name": "TestOutput",
  invalid json here
}
```
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue(); // Should still succeed with graceful degradation
        var card = result.Value;
        _ = card.OutputSchema.Should().BeNull(); // Schema should be null due to invalid JSON
    }

    [Fact]
    public void ParseSectionsShouldHandleAllStandardSections()
    {
        // Arrange
        const string content =
            """
---
agent: "sections-test"
---

# ROLE
Role content

# OBJECTIVE
Objective content

# CONTEXT
Context content

# TOOLS
Tools content

# CONSTRAINTS
Constraints content

# STYLE
Style content

# EXAMPLES
Examples content
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();
        var card = result.Value;
        _ = card.Sections.Should().HaveCount(7);
        _ = card.Sections["ROLE"].Should().Be("Role content");
        _ = card.Sections["OBJECTIVE"].Should().Be("Objective content");
        _ = card.Sections["CONTEXT"].Should().Be("Context content");
        _ = card.Sections["TOOLS"].Should().Be("Tools content");
        _ = card.Sections["CONSTRAINTS"].Should().Be("Constraints content");
        _ = card.Sections["STYLE"].Should().Be("Style content");
        _ = card.Sections["EXAMPLES"].Should().Be("Examples content");
    }

    [Fact]
    public void ParseSectionsShouldHandleUnknownSections()
    {
        // Arrange
        const string content =
            """
---
agent: "custom-sections-test"
---

# ROLE
Standard role

# CUSTOM SECTION
This is a custom section that should still be parsed

# ANOTHER CUSTOM
Another custom section content
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsSuccess.Should().BeTrue();
        var card = result.Value;
        _ = card.Sections.Should().ContainKey("CUSTOM SECTION");
        _ = card.Sections["CUSTOM SECTION"]
            .Should()
            .Be("This is a custom section that should still be parsed");
        _ = card.Sections.Should().ContainKey("ANOTHER CUSTOM");
        _ = card.Sections["ANOTHER CUSTOM"].Should().Be("Another custom section content");
    }

    [Fact]
    public void ParseAgentCardWithMissingAgentIdShouldReturnFailure()
    {
        // Arrange
        const string content =
            """
---
name: "No Agent ID"
---

# ROLE
Test
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Be("Agent ID is required in metadata");
    }

    [Fact]
    public void LoadFromFileWithNonExistentFileShouldReturnFailure()
    {
        // Act
        var result = AgentCardParser.LoadFromFile("/nonexistent/file.agent.md");

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Contain("Agent card file not found");
    }

    [Fact]
    public void LoadFromFileWithEmptyPathShouldReturnFailure()
    {
        // Act
        var result = AgentCardParser.LoadFromFile("");

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Be("File path cannot be empty");
    }

    [Fact]
    public void ParseAgentCardWithInvalidYamlShouldReturnFailure()
    {
        // Arrange
        const string content =
            """
---
agent: "test-agent"
invalid yaml syntax here
  - this is not valid
---

# ROLE
Test
""";

        // Act
        var result = _parser.ParseAgentCard(content);

        // Assert
        _ = result.IsFailure.Should().BeTrue();
        _ = result.Error.Should().Contain("Invalid YAML format");
    }

    [Fact]
    public void SectionHandlerRegistryShouldBeExtensible()
    {
        // Arrange
        var registry = new SectionHandlerRegistry();
        var customHandler = new TestCustomSectionHandler();

        // Act
        registry.Register(customHandler);
        var handler = registry.GetHandler("TEST CUSTOM");

        // Assert
        _ = handler.Should().NotBeNull();
        _ = handler.Should().Be(customHandler);
    }

    [Fact]
    public void ResultMapShouldTransformSuccessValue()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var mapped = result.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        _ = mapped.IsSuccess.Should().BeTrue();
        _ = mapped.Value.Should().Be("42");
    }

    [Fact]
    public void ResultMapShouldPropagateFailure()
    {
        // Arrange
        var result = Result<int>.Failure("Error message");

        // Act
        var mapped = result.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        _ = mapped.IsFailure.Should().BeTrue();
        _ = mapped.Error.Should().Be("Error message");
    }

    [Fact]
    public void ResultBindShouldChainOperations()
    {
        // Arrange
        var result = Result<int>.Success(10);

        // Act
        var chained = result.Bind(x =>
            x > 5
                ? Result<string>.Success($"Value is {x}")
                : Result<string>.Failure("Value too small")
        );

        // Assert
        _ = chained.IsSuccess.Should().BeTrue();
        _ = chained.Value.Should().Be("Value is 10");
    }

    private sealed class TestCustomSectionHandler : ISectionHandler
    {
        public string SectionName => "TEST CUSTOM";

        public Result ProcessSection(string content, AgentCard card)
        {
            card.Sections[SectionName] = content;
            return Result.Success();
        }
    }
}
