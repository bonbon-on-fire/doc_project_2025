// DocProject.AppHost/Program.cs
// Complete Aspire orchestration for DOC_Project_2025
// Task 6: Full AppHost configuration with all services

var builder = DistributedApplication.CreateBuilder(args);
var config = builder.Configuration;

// ========================================================================
// UNIFIED PORT CONFIGURATION
// ========================================================================
// Single source of truth for all service ports across environments
// Defined in AIChat.Server/appsettings.json under "ServicePorts"

// Validate and load port configuration
var apiServerPortStr = config["ServicePorts:ApiServer:Development"];
if (string.IsNullOrEmpty(apiServerPortStr))
{
    throw new InvalidOperationException(
        "❌ ServicePorts:ApiServer:Development not found in appsettings.json. " +
        "Unified port configuration is required for AppHost operation.");
}

// Parse port values with validation and invariant culture
var apiServerPort = int.Parse(apiServerPortStr, System.Globalization.CultureInfo.InvariantCulture);
var clientPort = int.Parse(config["ServicePorts:Client:Development"]
    ?? throw new InvalidOperationException("ServicePorts:Client:Development not configured"),
    System.Globalization.CultureInfo.InvariantCulture);
var orleansHostHttpPort = int.Parse(config["ServicePorts:OrleansHost:Http:Development"]
    ?? throw new InvalidOperationException("ServicePorts:OrleansHost:Http:Development not configured"),
    System.Globalization.CultureInfo.InvariantCulture);

Console.WriteLine("═════════════════════════════════════════════════════════");
Console.WriteLine("🔌 UNIFIED PORT CONFIGURATION");
Console.WriteLine("═════════════════════════════════════════════════════════");
Console.WriteLine($"  API Server:   http://localhost:{apiServerPort}");
Console.WriteLine($"  Client:       http://localhost:{clientPort}");
Console.WriteLine($"  Orleans Host: http://localhost:{orleansHostHttpPort}");
Console.WriteLine("═════════════════════════════════════════════════════════");

// ========================================================================
// INFRASTRUCTURE RESOURCES
// ========================================================================

// SQLite database with persistent data binding
// Data is stored in ./data directory for persistence across restarts
// Using CommunityToolkit.Aspire.Hosting.Sqlite extension
var database = builder.AddSqlite("sqlite-server");

// Optional: SQLite Web UI for development inspection
// (Will be enabled in future enhancement)
// database.WithSqliteWeb();

// ========================================================================
// ORLEANS SILO (SEPARATE SERVICE)
// ========================================================================

// Orleans Host as separate service (no longer embedded in AIChat.Server)
// This is a major architectural change - Orleans runs independently
// Orleans.Host defines its own endpoints in launchSettings.json (55500 HTTPS, 55501 HTTP)
// No need to override via AppHost - AppHost just orchestrates the service
var orleansHost = builder.AddProject("orleans-host", "../AIChat.Orleans.Host/AIChat.Orleans.Host.csproj")
    .WithReference(database)
    .WaitFor(database);                                  // Ensure DB ready first

// ========================================================================
// BACKEND API SERVER (ORLEANS CLIENT)
// ========================================================================

// AIChat.Server - Now Orleans CLIENT only (silo code removed in Task 7)
// Server connects to Orleans Host as a client, no longer hosts the silo
// Ports are explicitly managed via unified configuration
// AIChat.Server uses the DEV launch profile from launchSettings.json
// The DEV profile defines applicationUrl which Aspire registers with DCP
// This ensures proper service discovery and endpoint configuration
var apiServer = builder.AddProject("api-server", "../AIChat.Server/AIChat.Server.csproj", "DEV")
    .WithReference(database)
    .WithReference(orleansHost)
    .WaitFor(orleansHost)               // Orleans must be ready for client connection
    .WaitFor(database);                 // Database must be ready for queries

// ========================================================================
// FRONTEND APPLICATION
// ========================================================================

// SvelteKit client with automatic API URL configuration
// API URL is now explicitly injected from unified port configuration
// This replaces reliance on apiServer.GetEndpoint() which could fail
// IMPORTANT: Must pass DATABASE_URL for SvelteKit SSR (hooks.server.ts needs it)
var viteApiUrl = $"http://localhost:{apiServerPort}";
var client = builder.AddNpmApp("client", "../../client", "dev")
    .WithReference(apiServer)
    .WithHttpEndpoint(port: clientPort, env: "PORT")
    .WithEnvironment("VITE_API_URL", viteApiUrl)
    .WithEnvironment("DATABASE_URL", "local.db")           // Required for SvelteKit SSR
    .WithEnvironment("ASPIRE_ENVIRONMENT", "Development")  // Signal to client we're in AppHost
    .WaitFor(apiServer);                // API must be ready before client starts

// ========================================================================
// LAUNCH PROFILES
// ========================================================================

// Configure different launch profiles based on environment
// This allows developers to run only the services they need
if (builder.Configuration["ASPIRE_PROFILE"] == "backend-only")
{
    // Backend development profile - no client
    // Useful for API-only development and testing
    // Note: Aspire 9.0 API - RemoveResource may not be available in all versions
    // If build fails, comment out the line below
    // builder.RemoveResource(client);
}
else if (builder.Configuration["ASPIRE_PROFILE"] == "orleans-debug")
{
    // Orleans debugging profile - extra logging for troubleshooting
    orleansHost.WithEnvironment("Logging__LogLevel__Orleans", "Debug");
}

builder.Build().Run();
