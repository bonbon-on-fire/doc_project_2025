using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AIChat.Server.Services;
using AIChat.Server.Services.AgentCards;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services
{
    public class AgentCardParserTests
    {
        private readonly AgentCardParser _parser;
        private readonly Mock<ILogger<AgentCardParser>> _loggerMock;

        public AgentCardParserTests()
        {
            _loggerMock = new Mock<ILogger<AgentCardParser>>();
            _parser = new AgentCardParser(_loggerMock.Object);
        }

        [Fact]
        public void ParseAgentCard_WithValidCompleteCard_ShouldParseAllFields()
        {
            // Arrange
            var content = @"---
agent: ""test-agent""
name: ""Test Agent""
version: ""1.0.0""
category: ""testing""
model_hints:
  - ""gpt-4""
  - ""claude-3""
capabilities:
  tools:
    - ""search""
    - ""edit""
  memory: ""episodic""
  max_tokens: 4096
output_contract: ""json_schema""
risk_level: ""low""
tags:
  - ""test""
  - ""example""
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
  ""name"": ""TestSchema"",
  ""schema"": {
    ""type"": ""object"",
    ""properties"": {
      ""test"": { ""type"": ""string"" }
    }
  }
}
```";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Error.Should().BeNull();
            
            var card = result.Value;
            card.Should().NotBeNull();
            card.Metadata.Agent.Should().Be("test-agent");
            card.Metadata.Name.Should().Be("Test Agent");
            card.Metadata.Version.Should().Be("1.0.0");
            card.Metadata.Category.Should().Be("testing");
            card.Metadata.ModelHints.Should().BeEquivalentTo(new[] { "gpt-4", "claude-3" });
            card.Metadata.Capabilities.Tools.Should().BeEquivalentTo(new[] { "search", "edit" });
            card.Metadata.Capabilities.Memory.Should().Be("episodic");
            card.Metadata.Capabilities.MaxTokens.Should().Be(4096);
            card.Metadata.OutputContract.Should().Be("json_schema");
            card.Metadata.RiskLevel.Should().Be("low");
            card.Metadata.Tags.Should().BeEquivalentTo(new[] { "test", "example" });
            
            card.Role.Should().Be("You are a test agent for validation.");
            card.Objective.Should().Be("Validate the parser functionality.");
            card.Workflow.Should().HaveCount(2);
            card.OutputSchema.Should().NotBeNull();
        }

        [Fact]
        public void ParseAgentCard_WithMinimalCard_ShouldParseRequiredFields()
        {
            // Arrange
            var content = @"---
agent: ""minimal-agent""
name: ""Minimal Agent""
---

# ROLE
A minimal test agent.

# OBJECTIVE
Test minimal configuration.";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            
            var card = result.Value;
            card.Should().NotBeNull();
            card.Metadata.Agent.Should().Be("minimal-agent");
            card.Metadata.Name.Should().Be("Minimal Agent");
            card.Metadata.Version.Should().Be("1.0.0"); // Default
            card.Metadata.Category.Should().Be("general"); // Default
            card.Role.Should().Be("A minimal test agent.");
            card.Objective.Should().Be("Test minimal configuration.");
        }

        [Fact]
        public void ParseAgentCard_WithoutFrontMatter_ShouldReturnFailure()
        {
            // Arrange
            var content = @"# ROLE
Some role without front matter";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be("Agent card must have YAML front matter");
        }

        [Fact]
        public void ParseAgentCard_WithEmptyContent_ShouldReturnFailure()
        {
            // Act
            var result = _parser.ParseAgentCard("");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be("Agent card content cannot be empty");
        }

        [Fact]
        public void ParseAgentCard_WithNullContent_ShouldReturnFailure()
        {
            // Act
            var result = _parser.ParseAgentCard(null!);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be("Agent card content cannot be empty");
        }

        [Fact]
        public void ToMode_ShouldConvertCorrectly()
        {
            // Arrange
            var content = @"---
agent: ""converter-test""
name: ""Converter Test""
category: ""development""
capabilities:
  tools:
    - ""tool1""
    - ""tool2""
model_hints:
  - ""gpt-4""
---

# ROLE
Test conversion agent.

# OBJECTIVE
Convert to Mode object.

# CONSTRAINTS
- Constraint 1
- Constraint 2";

            // Act
            var result = _parser.ParseAgentCard(content);
            var card = result.Value;
            var mode = card.ToMode();

            // Assert
            mode.Should().NotBeNull();
            mode.Id.Should().Be("converter-test");
            mode.Name.Should().Be("Converter Test");
            mode.Description.Should().Be("Convert to Mode object.");
            mode.Category.Should().Be("development");
            mode.Tools.Should().BeEquivalentTo(new[] { "tool1", "tool2" });
            mode.DefaultModel.Should().Be("gpt-4");
            mode.IsSystem.Should().BeFalse();
            mode.Prompt.Should().Contain("Test conversion agent");
            mode.Prompt.Should().Contain("Objective: Convert to Mode object");
            mode.Prompt.Should().Contain("CONSTRAINTS");
        }

        [Fact]
        public void ParseWorkflow_WithNumberedSteps_ShouldExtractSteps()
        {
            // Arrange
            var content = @"---
agent: ""workflow-test""
---

# ROLE
Test agent

# WORKFLOW
1. First step in process
2. Second step with details
3. Third and final step
Some additional text that's not a step";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var card = result.Value;
            card.Workflow.Should().HaveCount(3);
            card.Workflow[0].Should().Be("1. First step in process");
            card.Workflow[1].Should().Be("2. Second step with details");
            card.Workflow[2].Should().Be("3. Third and final step");
        }

        [Fact]
        public void ParseJsonSchema_WithValidSchema_ShouldParseCorrectly()
        {
            // Arrange
            var content = @"---
agent: ""schema-test""
---

# ROLE
Test

# OUTPUT SCHEMA
```json
{
  ""name"": ""TestOutput"",
  ""schema"": {
    ""type"": ""object"",
    ""required"": [""field1""],
    ""properties"": {
      ""field1"": {
        ""type"": ""string"",
        ""description"": ""Test field""
      }
    }
  }
}
```";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var card = result.Value;
            card.OutputSchema.Should().NotBeNull();
            var root = card.OutputSchema.RootElement;
            root.GetProperty("name").GetString().Should().Be("TestOutput");
            root.GetProperty("schema").GetProperty("type").GetString().Should().Be("object");
        }

        [Fact]
        public void ParseJsonSchema_WithInvalidJson_ShouldGracefullyDegrade()
        {
            // Arrange
            var content = @"---
agent: ""invalid-schema-test""
---

# ROLE
Test

# OUTPUT SCHEMA
```json
{
  ""name"": ""TestOutput"",
  invalid json here
}
```";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue(); // Should still succeed with graceful degradation
            var card = result.Value;
            card.OutputSchema.Should().BeNull(); // Schema should be null due to invalid JSON
        }

        [Fact]
        public void ParseSections_ShouldHandleAllStandardSections()
        {
            // Arrange
            var content = @"---
agent: ""sections-test""
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
Examples content";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var card = result.Value;
            card.Sections.Should().HaveCount(7);
            card.Sections["ROLE"].Should().Be("Role content");
            card.Sections["OBJECTIVE"].Should().Be("Objective content");
            card.Sections["CONTEXT"].Should().Be("Context content");
            card.Sections["TOOLS"].Should().Be("Tools content");
            card.Sections["CONSTRAINTS"].Should().Be("Constraints content");
            card.Sections["STYLE"].Should().Be("Style content");
            card.Sections["EXAMPLES"].Should().Be("Examples content");
        }

        [Fact]
        public void ParseSections_ShouldHandleUnknownSections()
        {
            // Arrange
            var content = @"---
agent: ""custom-sections-test""
---

# ROLE
Standard role

# CUSTOM SECTION
This is a custom section that should still be parsed

# ANOTHER CUSTOM
Another custom section content";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var card = result.Value;
            card.Sections.Should().ContainKey("CUSTOM SECTION");
            card.Sections["CUSTOM SECTION"].Should().Be("This is a custom section that should still be parsed");
            card.Sections.Should().ContainKey("ANOTHER CUSTOM");
            card.Sections["ANOTHER CUSTOM"].Should().Be("Another custom section content");
        }

        [Fact]
        public void ParseAgentCard_WithMissingAgentId_ShouldReturnFailure()
        {
            // Arrange
            var content = @"---
name: ""No Agent ID""
---

# ROLE
Test";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be("Agent ID is required in metadata");
        }

        [Fact]
        public void LoadFromFile_WithNonExistentFile_ShouldReturnFailure()
        {
            // Act
            var result = AgentCardParser.LoadFromFile("/nonexistent/file.agent.md");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Contain("Agent card file not found");
        }

        [Fact]
        public void LoadFromFile_WithEmptyPath_ShouldReturnFailure()
        {
            // Act
            var result = AgentCardParser.LoadFromFile("");

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be("File path cannot be empty");
        }

        [Fact]
        public void ParseAgentCard_WithInvalidYaml_ShouldReturnFailure()
        {
            // Arrange
            var content = @"---
agent: ""test-agent""
invalid yaml syntax here
  - this is not valid
---

# ROLE
Test";

            // Act
            var result = _parser.ParseAgentCard(content);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Error.Should().Contain("Invalid YAML format");
        }

        [Fact]
        public void SectionHandlerRegistry_ShouldBeExtensible()
        {
            // Arrange
            var registry = new SectionHandlerRegistry();
            var customHandler = new TestCustomSectionHandler();
            
            // Act
            registry.Register(customHandler);
            var handler = registry.GetHandler("TEST CUSTOM");
            
            // Assert
            handler.Should().NotBeNull();
            handler.Should().Be(customHandler);
        }

        [Fact]
        public void Result_Map_ShouldTransformSuccessValue()
        {
            // Arrange
            var result = Result<int>.Success(42);
            
            // Act
            var mapped = result.Map(x => x.ToString());
            
            // Assert
            mapped.IsSuccess.Should().BeTrue();
            mapped.Value.Should().Be("42");
        }

        [Fact]
        public void Result_Map_ShouldPropagateFailure()
        {
            // Arrange
            var result = Result<int>.Failure("Error message");
            
            // Act
            var mapped = result.Map(x => x.ToString());
            
            // Assert
            mapped.IsFailure.Should().BeTrue();
            mapped.Error.Should().Be("Error message");
        }

        [Fact]
        public void Result_Bind_ShouldChainOperations()
        {
            // Arrange
            var result = Result<int>.Success(10);
            
            // Act
            var chained = result.Bind(x => 
                x > 5 
                    ? Result<string>.Success($"Value is {x}") 
                    : Result<string>.Failure("Value too small"));
            
            // Assert
            chained.IsSuccess.Should().BeTrue();
            chained.Value.Should().Be("Value is 10");
        }

        private class TestCustomSectionHandler : ISectionHandler
        {
            public string SectionName => "TEST CUSTOM";
            
            public Result ProcessSection(string content, AgentCard card)
            {
                card.Sections[SectionName] = content;
                return Result.Success();
            }
        }
    }
}