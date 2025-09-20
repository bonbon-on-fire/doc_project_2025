using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AIChat.Server.Tests.Api;

public class ModeControllerSimpleTest(WebApplicationFactory<Program> factory) : BaseApiTest(factory)
{
    [Fact]
    public async Task ModeControllerIsAccessible()
    {
        // Arrange
        var client = Factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/mode?userId=test123");

        // Assert
        _ = response
            .StatusCode.Should()
            .NotBe(HttpStatusCode.NotFound, "The /api/mode endpoint should be accessible");

        // Log actual status for debugging
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"404 Response: {content}");
        }
    }
}
