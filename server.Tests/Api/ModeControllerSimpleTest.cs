using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

namespace AIChat.Server.Tests.Api;

public class ModeControllerSimpleTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ModeControllerSimpleTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        });
    }

    [Fact]
    public async Task ModeController_IsAccessible()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.GetAsync("/api/mode?userId=test123");
        
        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, 
            "The /api/mode endpoint should be accessible");
        
        // Log actual status for debugging
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"404 Response: {content}");
        }
    }
}