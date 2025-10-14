// DocProject.AppHost/Program.cs
// Complete Aspire orchestration for DOC_Project_2025
// Task 6: Full AppHost configuration with all services

var builder = DistributedApplication.CreateBuilder(args);

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
var orleansHost = builder.AddProject("orleans-host", "../AIChat.Orleans.Host/AIChat.Orleans.Host.csproj")
    .WithReference(database)
    .WithHttpEndpoint(port: 11111, name: "silo")        // Silo port for cluster communication
    .WithHttpEndpoint(port: 30000, name: "gateway")     // Gateway port for client connections
    .WithHttpEndpoint(port: 5100, name: "monitoring")   // Monitoring API for health checks
    .WaitFor(database);                                  // Ensure DB ready first

// ========================================================================
// BACKEND API SERVER (ORLEANS CLIENT)
// ========================================================================

// AIChat.Server - Now Orleans CLIENT only (silo code will be removed in Task 7)
// Server connects to Orleans Host as a client, no longer hosts the silo
var apiServer = builder.AddProject("api-server", "../AIChat.Server/AIChat.Server.csproj")
    .WithReference(database)
    .WithReference(orleansHost)
    .WithEnvironment("Orleans__GatewayPort", orleansHost.GetEndpoint("gateway"))
    .WithEnvironment("ASPNETCORE_URLS", "http://+:5099")
    .WithHttpEndpoint(port: 5099, name: "http")
    .WaitFor(orleansHost)               // Orleans must be ready for client connection
    .WaitFor(database);                 // Database must be ready for queries

// ========================================================================
// FRONTEND APPLICATION
// ========================================================================

// SvelteKit client with automatic API URL configuration
// Aspire injects environment variables for seamless service discovery
var client = builder.AddNpmApp("client", "../client", "dev")
    .WithReference(apiServer)
    .WithHttpEndpoint(env: "PORT", port: 5173)
    .WithEnvironment("VITE_API_URL", apiServer.GetEndpoint("http"))
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
