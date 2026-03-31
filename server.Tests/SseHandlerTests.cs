// <copyright file="SseHandlerTests.cs" company="AIChat">
// Copyright (c) AIChat. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Server.Services.TestMode;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests;

/// <summary>
/// Tests for the Server-Sent Events (SSE) handler functionality.
/// </summary>
public class SseHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds an HTTP request message for testing SSE endpoints.
    /// </summary>
    /// <param name="userMessage">The user message to include in the request.</param>
    /// <param name="stream">Whether to enable streaming.</param>
    /// <param name="model">The model to use for the request.</param>
    /// <returns>An HTTP request message configured for testing.</returns>
    private static HttpRequestMessage BuildRequest(string userMessage, bool stream = true, string? model = null)
    {
        var payload = new
        {
            model,
            stream,
            messages = new object[]
            {
                new { role = "user", content = userMessage }
            }
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return req;
    }

    [Fact]
    public async Task NonTargetedPath_Returns404()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/completions");
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingStreamTrue_Returns404()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello", stream: false);
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Response_IsEventStream_WithChunks_AndDone()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 3 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello world", stream: true);
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var text = await res.Content.ReadAsStringAsync();
        text.Should().Contain("data:");
        text.Should().Contain("[DONE]");

        var firstJson = GetFirstJsonLine(text);
        using var doc = JsonDocument.Parse(firstJson);
        var root = doc.RootElement;
        root.GetProperty("object").GetString().Should().Be("chat.completion.chunk");
        root.GetProperty("choices")[0].GetProperty("index").GetInt32().Should().Be(0);
        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("created", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Model_IsEchoed_WhenProvided()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello", stream: true, model: "test-model");
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var firstJson = GetFirstJsonLine(text);
        using var doc = JsonDocument.Parse(firstJson);
        doc.RootElement.GetProperty("model").GetString().Should().Be("test-model");
    }

    [Fact]
    public async Task Lorem_Count_WithinRange_And_EchoExcluded()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 50 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("test-echo", stream: true);
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        var allJson = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var contents = allJson
            .SelectMany(d => d.RootElement.GetProperty("choices")[0].GetProperty("delta").EnumerateObject()
                .Where(p => p.NameEquals("content"))
                .Select(p => p.Value.GetString() ?? string.Empty))
            .ToList();

        contents.Count.Should().BeGreaterThan(0);
        var echo = "test-echo";
        var nonEchoWords = string.Join(" ", contents.Where(c => c != echo)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        nonEchoWords.Length.Should().BeGreaterThanOrEqualTo(5).And.BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task ReasoningFirst_WhenTriggered()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("Hello\nReason: please show thinking", stream: true);
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var jsons = GetAllJsonLines(text).ToList();

        var seenContent = false;
        var seenReasoning = false;
        foreach (var json in jsons.Skip(1))
        {
            using var doc = JsonDocument.Parse(json);
            var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var c) && !string.IsNullOrEmpty(c.GetString()))
            {
                seenContent = true;
            }

            if (delta.TryGetProperty("reasoning", out var r) && !string.IsNullOrEmpty(r.GetString()))
            {
                seenReasoning = true;
                if (seenContent)
                {
                    throw new Xunit.Sdk.XunitException("Reasoning appeared after content");
                }
            }
        }

        seenReasoning.Should().BeTrue();
    }

    [Fact]
    public async Task Request_Parsing_Ignores_Complex_Content_Arrays()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var payload = new
        {
            stream = true,
            messages = new object[]
            {
                new { role = "user", content = new object[] { new { type = "text", text = "ignored" } } },
                new { role = "user", content = "plain" }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var allJson = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var contents = allJson
                .SelectMany(d => d.RootElement.GetProperty("choices")[0].GetProperty("delta").EnumerateObject()
                    .Where(p => p.NameEquals("content"))
                    .Select(p => p.Value.GetString() ?? string.Empty))
                .ToList();
        contents.Where(c => !string.IsNullOrEmpty(c)).Last().Should().Be("<|user_post|><|text_message|> plain");
    }

    [Fact]
    public async Task Emits_Multiple_Chunks_Before_Done()
    {
        // Arrange: force small chunks and a long message to produce multiple chunks deterministically
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 3 };
        using var invoker = new HttpMessageInvoker(handler);
        var longMessage = string.Join(" ", Enumerable.Repeat("lorem", 30));
        var req = BuildRequest(longMessage, stream: true);

        // Act
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        // Assert
        var dataLines = text.Split('\n').Where(l => l.StartsWith("data: ", StringComparison.Ordinal)).ToList();
        dataLines.Count.Should().BeGreaterThan(3, "expected multiple SSE chunks to be emitted");
        text.Should().Contain("[DONE]");
    }

    [Fact]
    public async Task NoReasoning_WhenNotRequested()
    {
        // Arrange
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 5 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("Plain text only - no reasoning here", stream: true);

        // Act
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        // Assert: none of the JSON delta objects should include a 'reasoning' property
        var jsons = GetAllJsonLines(text).ToList();
        jsons.Should().NotBeEmpty();
        foreach (var json in jsons)
        {
            using var doc = JsonDocument.Parse(json);
            var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            delta.TryGetProperty("reasoning", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Pacing_ApproximatelyHonored_WithTolerance_NoReasoning()
    {
        // Arrange: choose small chunk size and non-reasoning message
        var handler = new TestSseMessageHandler { ChunkDelayMs = 50, WordsPerChunk = 5 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("pacing test without reasoning", stream: true);

        // Act
        var started = DateTime.UtcNow;
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var elapsed = DateTime.UtcNow - started;

        // Count lorem chunks (exclude pre/post markers)
        var jsons = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        int loremChunkCount = 0;
        foreach (var doc in jsons)
        {
            var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var c))
            {
                var str = c.GetString() ?? string.Empty;
                if (!str.Contains("<|user_pre|>", StringComparison.Ordinal) &&
                    !str.Contains("<|user_post|>", StringComparison.Ordinal))
                {
                    loremChunkCount++;
                }
            }
        }

        // Expected minimum duration equals chunkCount * delay
        var minMs = loremChunkCount * handler.ChunkDelayMs;
        // Allow generous overhead tolerance
        var toleranceMs = Math.Max(250, loremChunkCount * 10);

        elapsed.TotalMilliseconds.Should().BeGreaterThan(minMs - 25);
        elapsed.TotalMilliseconds.Should().BeLessThan(minMs + toleranceMs);
    }

    [Fact]
    public async Task WithReasoning_PreReasoningMarker_And_PostTextEcho_Present()
    {
        // Arrange
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 5 };
        using var invoker = new HttpMessageInvoker(handler);
        var message = "Hello there\nReason: please think first";
        var req = BuildRequest(message, stream: true);

        // Act
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        // Assert via parsed JSON: ensure reasoning appears and post text echo marker present in content
        var docs = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        bool sawReasoning = false;
        bool sawPostEcho = false;
        foreach (var d in docs)
        {
            var delta = d.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("reasoning", out var r) && !string.IsNullOrEmpty(r.GetString()))
            {
                sawReasoning = true;
            }
            if (delta.TryGetProperty("content", out var c))
            {
                var val = c.GetString() ?? string.Empty;
                if (val.Contains("<|user_post|><|text_message|>", StringComparison.Ordinal))
                {
                    sawPostEcho = true;
                }
            }
        }
        sawReasoning.Should().BeTrue();
        sawPostEcho.Should().BeTrue();
        text.Should().Contain("[DONE]");
    }

    [Fact]
    public async Task InstructionMode_ParsesBlock_And_IgnoresOutsideText()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 3 };
        using var invoker = new HttpMessageInvoker(handler);

        var instructions = """
<|instruction_start|>
{
  "id_message": "ID-XYZ",
  "messages": [
    { "text_message": { "length": 8 } }
  ]
}
<|instruction_end|>
""".Replace("\r", string.Empty);

        var userMessage = $"outside header {instructions} trailing";
        var req = BuildRequest(userMessage, stream: true);

        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        text.Should().Contain("ID-XYZ");
        text.Should().NotContain("outside header");
        text.Should().NotContain("trailing");
        text.Should().Contain("[DONE]");
    }

    [Fact]
    public async Task InstructionMode_MultipleMessages_ProduceDistinctIndices()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 2 };
        using var invoker = new HttpMessageInvoker(handler);

        var instructions = """
<|instruction_start|>
{
  "id_message": "ID-A",
  "messages": [
    { "text_message": { "length": 3 } },
    { "text_message": { "length": 2 } }
  ]
}
<|instruction_end|>
""".Replace("\r", string.Empty);

        var req = BuildRequest(instructions, stream: true);
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        var indices = GetAllJsonLines(text)
            .Select(s => JsonDocument.Parse(s))
            .Select(d => d.RootElement.GetProperty("choices")[0].GetProperty("index").GetInt32())
            .Distinct()
            .OrderBy(i => i)
            .ToArray();

        indices.Should().Equal(new[] { 0, 1 });
    }

    [Fact]
    public async Task InstructionMode_TextLength_Honored_IdMessage_Excluded()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 5 };
        using var invoker = new HttpMessageInvoker(handler);

        var instructions = """
<|instruction_start|>
{
  "id_message": "ID-MARK",
  "messages": [
    { "text_message": { "length": 7 } }
  ]
}
<|instruction_end|>
""".Replace("\r", string.Empty);

        var res = await invoker.SendAsync(BuildRequest(instructions, stream: true), default);
        var text = await res.Content.ReadAsStringAsync();

        var docs = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var contents = new List<string>();
        foreach (var d in docs)
        {
            var delta = d.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var c))
            {
                var str = c.GetString() ?? string.Empty;
                if (str == "ID-MARK") continue; // exclude id_message markers
                contents.Add(str);
            }
        }
        var words = string.Join(' ', contents).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words.Length.Should().Be(7);
    }

    [Fact(Skip = "Deferred until tool_call support is implemented")]
    public async Task InstructionMode_ToolCall_DeltaShape_And_Indexing()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);

        var instructions = """
<|instruction_start|>
{
  "id_message": "TC-ID",
  "messages": [
    { "tool_call": [ { "name": "search_web", "args": { "query": "q", "limit": 2 } } ] }
  ]
}
<|instruction_end|>
""".Replace("\r", string.Empty);

        var res = await invoker.SendAsync(BuildRequest(instructions, stream: true), default);
        var text = await res.Content.ReadAsStringAsync();

        var docs = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var indices = new HashSet<int>();
        var argConcat = new StringBuilder();
        var sawName = false;
        foreach (var d in docs)
        {
            var choice = d.RootElement.GetProperty("choices")[0];
            indices.Add(choice.GetProperty("index").GetInt32());
            var delta = choice.GetProperty("delta");
            if (delta.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
            {
                var entry = tc.EnumerateArray().First();
                var func = entry.GetProperty("function");
                if (func.TryGetProperty("name", out var nameEl) && nameEl.GetString() == "search_web")
                {
                    sawName = true;
                }
                if (func.TryGetProperty("arguments", out var argEl))
                {
                    argConcat.Append(argEl.GetString());
                }
            }
        }
        indices.Should().Equal(new[] { 0 });
        sawName.Should().BeTrue();
        // JSON of args without whitespace and in canonical order may vary; ensure both keys appear
        argConcat.ToString().Should().Contain("query");
        argConcat.ToString().Should().Contain("limit");
    }

    [Fact]
    public async Task InstructionMode_Fallback_On_InvalidJson()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);

        var invalid = "<|instruction_start|>{ not json }<|instruction_end|>";
        var res = await invoker.SendAsync(BuildRequest(invalid, stream: true), default);
        var text = await res.Content.ReadAsStringAsync();

        // Legacy should emit user_post marker (decoded) and DONE
        var docs = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        bool sawPostEcho = false;
        foreach (var d in docs)
        {
            var delta = d.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var c))
            {
                var val = c.GetString() ?? string.Empty;
                if (val.Contains("<|user_post|><|text_message|>", StringComparison.Ordinal))
                {
                    sawPostEcho = true;
                    break;
                }
            }
        }
        sawPostEcho.Should().BeTrue();
        text.Should().Contain("[DONE]");
    }

    /// <summary>
    /// Gets the first JSON line from the SSE response.
    /// </summary>
    /// <param name="response">The SSE response content.</param>
    /// <returns>The first JSON line found.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no JSON line is found.</exception>
    private static string GetFirstJsonLine(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && !line.Contains("[DONE]", StringComparison.Ordinal))
            {
                return line.Substring(6).Trim();
            }
        }

        throw new InvalidOperationException("No JSON line found");
    }

    /// <summary>
    /// Gets all JSON lines from the SSE response.
    /// </summary>
    /// <param name="response">The SSE response content.</param>
    /// <returns>An enumerable collection of JSON lines.</returns>
    private static IEnumerable<string> GetAllJsonLines(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && !line.Contains("[DONE]", StringComparison.Ordinal))
            {
                yield return line.Substring(6).Trim();
            }
        }
    }
}