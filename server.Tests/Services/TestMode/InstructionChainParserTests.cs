using System.Text.Json;
using AIChat.Server.Services.TestMode;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services.TestMode;

public class InstructionChainParserTests
{
    private readonly InstructionChainParser _parser;
    private readonly Mock<ILogger<InstructionChainParser>> _loggerMock;

    public InstructionChainParserTests()
    {
        _loggerMock = new Mock<ILogger<InstructionChainParser>>();
        _parser = new InstructionChainParser(_loggerMock.Object);
    }

    [Fact]
    public void ExtractInstructionChain_WithToolCall_ParsesSuccessfully()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "test-task-create",
                "messages": [
                    {
                        "tool_call": [
                            {
                                "name": "TaskManager_add_task",
                                "args": {
                                    "title": "Write E2E tests for task synchronization",
                                    "parentId": null
                                }
                            }
                        ]
                    }
                ]
            }
            <|instruction_end|>
            Let me add a task for writing E2E tests
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);

        var instruction = result[0];
        Assert.Equal("test-task-create", instruction.IdMessage);
        Assert.NotNull(instruction.Messages);
        _ = Assert.Single(instruction.Messages);

        var toolCallMessage = instruction.Messages[0];
        Assert.NotNull(toolCallMessage.ToolCalls);
        _ = Assert.Single(toolCallMessage.ToolCalls);

        var toolCall = toolCallMessage.ToolCalls[0];
        Assert.Equal("TaskManager_add_task", toolCall.Name);
        Assert.Contains("Write E2E tests for task synchronization", toolCall.ArgsJson);
    }

    [Fact]
    public void ExtractInstructionChain_WithMultipleToolCalls_ParsesAllCalls()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "multi-tool-test",
                "messages": [
                    {
                        "tool_call": [
                            {
                                "name": "tool1",
                                "args": {"param1": "value1"}
                            },
                            {
                                "name": "tool2",
                                "args": {"param2": "value2"}
                            }
                        ]
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);

        var instruction = result[0];
        Assert.Equal("multi-tool-test", instruction.IdMessage);
        _ = Assert.Single(instruction.Messages);

        var toolCallMessage = instruction.Messages[0];
        Assert.NotNull(toolCallMessage.ToolCalls);
        Assert.Equal(2, toolCallMessage.ToolCalls.Count);

        Assert.Equal("tool1", toolCallMessage.ToolCalls[0].Name);
        Assert.Contains("value1", toolCallMessage.ToolCalls[0].ArgsJson);

        Assert.Equal("tool2", toolCallMessage.ToolCalls[1].Name);
        Assert.Contains("value2", toolCallMessage.ToolCalls[1].ArgsJson);
    }

    [Fact]
    public void ExtractInstructionChain_WithTextAndToolMessages_ParsesBoth()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "mixed-messages",
                "messages": [
                    {
                        "text_message": {
                            "length": 100
                        }
                    },
                    {
                        "tool_call": [
                            {
                                "name": "some_tool",
                                "args": {}
                            }
                        ]
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);

        var instruction = result[0];
        Assert.Equal(2, instruction.Messages.Count);

        // First message should be text
        var textMessage = instruction.Messages[0];
        Assert.Equal(100, textMessage.TextLength);
        Assert.Null(textMessage.ToolCalls);

        // Second message should be tool call
        var toolMessage = instruction.Messages[1];
        Assert.Null(toolMessage.TextLength);
        Assert.NotNull(toolMessage.ToolCalls);
        _ = Assert.Single(toolMessage.ToolCalls);
        Assert.Equal("some_tool", toolMessage.ToolCalls[0].Name);
    }

    [Fact]
    public void ExtractInstructionChain_WithInstructionChainArray_ParsesMultipleInstructions()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "instruction_chain": [
                    {
                        "id_message": "first",
                        "messages": [
                            {
                                "text_message": {
                                    "length": 50
                                }
                            }
                        ]
                    },
                    {
                        "id_message": "second",
                        "messages": [
                            {
                                "tool_call": [
                                    {
                                        "name": "tool1",
                                        "args": {}
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);

        Assert.Equal("first", result[0].IdMessage);
        _ = Assert.Single(result[0].Messages);
        Assert.Equal(50, result[0].Messages[0].TextLength);

        Assert.Equal("second", result[1].IdMessage);
        _ = Assert.Single(result[1].Messages);
        Assert.Equal("tool1", result[1].Messages[0].ToolCalls![0].Name);
    }

    [Fact]
    public void ExtractInstructionChain_WithReasoningField_ParsesReasoningLength()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "with-reasoning",
                "reasoning": {
                    "length": 500
                },
                "messages": [
                    {
                        "text_message": {
                            "length": 100
                        }
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);
        Assert.Equal(500, result[0].ReasoningLength);
    }

    [Fact]
    public void ExtractInstructionChain_WithNullInput_ReturnsNull()
    {
        // Act
        var result = _parser.ExtractInstructionChain(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractInstructionChain_WithEmptyString_ReturnsNull()
    {
        // Act
        var result = _parser.ExtractInstructionChain(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractInstructionChain_WithMissingTags_ReturnsNull()
    {
        // Arrange
        var input = /*lang=json,strict*/ """
            {
                "id_message": "test",
                "messages": []
            }
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractInstructionChain_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            { invalid json }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractInstructionChain_WithEmptyMessagesArray_ReturnsNull()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "empty",
                "messages": []
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseSingleInstruction_WithValidJsonElement_ReturnsInstructionPlan()
    {
        // Arrange
        var json = """
            {
                "id_message": "test",
                "messages": [
                    {
                        "tool_call": [
                            {
                                "name": "test_tool",
                                "args": {"key": "value"}
                            }
                        ]
                    }
                ]
            }
            """;
        using var doc = JsonDocument.Parse(json);

        // Act
        var result = _parser.ParseSingleInstruction(doc.RootElement);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.IdMessage);
        _ = Assert.Single(result.Messages);
        Assert.Equal("test_tool", result.Messages[0].ToolCalls![0].Name);
    }

    [Fact]
    public void ExtractInstructionChain_WithComplexNestedArgs_PreservesJsonStructure()
    {
        // Arrange
        var input = """
            <|instruction_start|>
            {
                "id_message": "complex-args",
                "messages": [
                    {
                        "tool_call": [
                            {
                                "name": "ComplexTool",
                                "args": {
                                    "nested": {
                                        "deeply": {
                                            "value": "test",
                                            "array": [1, 2, 3]
                                        }
                                    },
                                    "nullValue": null,
                                    "boolValue": true
                                }
                            }
                        ]
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);

        var toolCall = result[0].Messages[0].ToolCalls![0];
        Assert.Equal("ComplexTool", toolCall.Name);

        // Parse the ArgsJson to verify structure is preserved
        using var argsDoc = JsonDocument.Parse(toolCall.ArgsJson);
        var root = argsDoc.RootElement;

        Assert.True(root.TryGetProperty("nested", out var nested));
        Assert.True(nested.TryGetProperty("deeply", out var deeply));
        Assert.Equal("test", deeply.GetProperty("value").GetString());

        var array = deeply.GetProperty("array");
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(3, array.GetArrayLength());
    }

    [Theory]
    [InlineData("tool_result")]
    [InlineData("unknown_message_type")]
    public void ExtractInstructionChain_WithUnsupportedMessageTypes_SkipsThoseMessages(
        string messageType
    )
    {
        // Arrange
        var input = $$$"""
            <|instruction_start|>
            {
                "id_message": "test-unsupported",
                "messages": [
                    {
                        "tool_call": [
                            {
                                "name": "supported_tool",
                                "args": {}
                            }
                        ]
                    },
                    {
                        "{{{messageType}}}": {
                            "data": "some data"
                        }
                    },
                    {
                        "text_message": {
                            "length": 50
                        }
                    }
                ]
            }
            <|instruction_end|>
            """;

        // Act
        var result = _parser.ExtractInstructionChain(input);

        // Assert
        Assert.NotNull(result);
        _ = Assert.Single(result);

        // Should only parse tool_call and text_message, skipping unsupported types
        Assert.Equal(2, result[0].Messages.Count);
        Assert.NotNull(result[0].Messages[0].ToolCalls);
        Assert.Equal(50, result[0].Messages[1].TextLength);
    }
}
