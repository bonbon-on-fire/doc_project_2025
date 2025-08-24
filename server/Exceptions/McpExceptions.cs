using System;

namespace AIChat.Server.Exceptions;

/// <summary>
/// Base exception for all MCP-related errors
/// </summary>
public class McpException : Exception
{
    public string? ServerName { get; }
    
    public McpException(string message) : base(message)
    {
    }
    
    public McpException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
    
    public McpException(string message, string serverName) : base(message)
    {
        ServerName = serverName;
    }
    
    public McpException(string message, string serverName, Exception innerException) 
        : base(message, innerException)
    {
        ServerName = serverName;
    }
}

/// <summary>
/// Exception thrown when MCP configuration is invalid
/// </summary>
public class McpConfigurationException : McpException
{
    public string? ConfigSection { get; }
    
    public McpConfigurationException(string message) : base(message)
    {
    }
    
    public McpConfigurationException(string message, string configSection) : base(message)
    {
        ConfigSection = configSection;
    }
    
    public McpConfigurationException(string message, string configSection, Exception innerException) 
        : base(message, innerException)
    {
        ConfigSection = configSection;
    }
}

/// <summary>
/// Exception thrown when MCP client initialization fails
/// </summary>
public class McpInitializationException : McpException
{
    public McpInitializationException(string message, string serverName) 
        : base(message, serverName)
    {
    }
    
    public McpInitializationException(string message, string serverName, Exception innerException) 
        : base(message, serverName, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when MCP transport creation fails
/// </summary>
public class McpTransportException : McpException
{
    public string TransportType { get; }
    
    public McpTransportException(string message, string serverName, string transportType) 
        : base(message, serverName)
    {
        TransportType = transportType;
    }
    
    public McpTransportException(string message, string serverName, string transportType, Exception innerException) 
        : base(message, serverName, innerException)
    {
        TransportType = transportType;
    }
}

/// <summary>
/// Exception thrown when MCP client connection fails
/// </summary>
public class McpConnectionException : McpException
{
    public McpConnectionException(string message, string serverName) 
        : base(message, serverName)
    {
    }
    
    public McpConnectionException(string message, string serverName, Exception innerException) 
        : base(message, serverName, innerException)
    {
    }
}