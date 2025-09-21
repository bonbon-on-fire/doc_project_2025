# EXCEPTION HANDLING
<!-- Exception handling patterns and best practices -->

## REQUIRED: Exception Usage Patterns
- **CONDITION**: When implementing error handling in methods
- **STRUCTURE**: try/catch/finally blocks should be the primary content of a method
- **PURPOSE**: Use only for handling or logging exceptions, not control flow
- **FORBIDDEN**: Using exceptions for normal program flow control
- **CATCH SPECIFIC**: Only catch specific exception types
- **AVOID GENERAL**: Do not catch general Exception except in specific top-level scenarios
- **LEGACY CODE**: Do not modify existing general exception catching, but avoid in new code
- **ENFORCEMENT SCOPE**: Apply to all new exception handling implementation
- **COMPLIANCE OUTCOME**: Improved error handling, better debugging, and maintainable exception patterns

**Implementation Examples:**
```csharp
//Good - specific exception handling
try
{
    return await ProcessData(data);
}
catch (ArgumentException ex)
{
    logger.LogError(ex, "Invalid argument provided");
    throw;
}
catch (HttpRequestException ex)
{
    logger.LogError(ex, "Network error occurred");
    return defaultResult;
}

// Avoid - general exception catching (except in specific top-level scenarios)
catch (Exception ex) { ... }
