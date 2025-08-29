# LINQ AND COLLECTIONS
<!-- LINQ usage patterns and collection handling standards -->

## PREFERRED: LINQ Usage
- **DEFAULT**: Use LINQ unless it hurts readability or performance significantly
- **SELECTION**: Use `Select()` over `SelectMany()` when possible
- **SAFETY**: Use `FirstOrDefault()` over `First()` when result may be empty
- **EXISTENCE**: Use `Any()` instead of `Count() > 0` for existence checks

**Implementation Examples:**
```csharp
// Good - using Any() for existence
if (items.Any(x => x.IsActive)) { ... }

// Avoid - using Count() for existence
if (items.Count(x => x.IsActive) > 0) { ... }
```

## REQUIRED: Method Parameters
- **FORBIDDEN**: `List<T>` parameters - use `IEnumerable<T>` instead
- **FORBIDDEN**: `Dictionary<K,V>` parameters - use `IDictionary<K,V>` instead
- **PREFERRED**: `IEnumerable<T>` over `IQueryable<T>` except for LINQ to SQL/Entities
- **FORMATTING**: If any parameter is on new line, put ALL parameters on separate lines
- **TRAILING COMMAS**: Always use trailing commas in multiline initializers
- **USING DIRECTIVES**: Always include necessary `using` directives to prevent compilation errors

**Implementation Examples:**
```csharp
// Good - interface parameters
public void ProcessItems(IEnumerable<string> items) { ... }

// Avoid - concrete collection parameters
public void ProcessItems(List<string> items) { ... }
