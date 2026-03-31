# CODE QUALITY REQUIREMENTS
<!-- Code quality standards, analyzers, and style enforcement -->

<!-- AI: DO NOT MODIFY THIS SECTION -->
## MANDATORY: Analyzer Requirements
- **WHITESPACE**: Eliminate unnecessary whitespace
- **THIS KEYWORD**: Always use `this.variable` and `this.method` when possible
- **COLLECTIONS**: Always use trailing commas in collections
- **CONSTANTS**: Use `const` instead of `readonly static` when possible
- **NULL COALESCING**: Use `??` instead of `if (x == null)` checks
- **NAMEOF**: Use `nameof()` instead of hardcoded strings (except when setting the variable itself)
- **FIELDS**: Use fields for private class members
- **FORBIDDEN**: Never use `!.` outside of Unit Tests
- **DYNAMIC**: Only use `dynamic` when absolutely necessary, prefer `object`
- **NAMESPACES**: Always use file-scoped namespaces
- **CODE STYLE**: You must **ALWAYS** follow the established code style guidelines. Format the document before running commands like `dotnet build` and `dotnet test`.

<!-- AI: DO NOT MODIFY THIS SECTION -->
## ROSLYN ANALYSIS
<!-- Static analysis and code style enforcement configuration -->
### REQUIRED: Roslyn Analyzer Configuration
- **ANALYZER ENABLEMENT**: All projects must use Roslyn Analyzers (EnableRoslynAnalyzers=true)
- **PACKAGES.CONFIG EXCLUSION**: Roslyn Analyzers are automatically disabled for projects with packages.config files
- **NOTARGETS SDK EXCLUSION**: Roslyn Analyzers are automatically disabled for projects using Microsoft.NoTargets.Sdk
- **TRAVERSAL SDK EXCLUSION**: Roslyn Analyzers are automatically disabled for projects using Microsoft.Traversal.Sdk
- **STYLECOP INTEGRATION**: StyleCop configuration file (stylecop.json) is automatically included as an additional file for all projects with Roslyn Analyzers enabled
- **SARIF LOGGING**: SARIF error logs are automatically generated with unique GUIDs for compliance tracking and Azure DevOps integration
- **BUILD WARNINGS**: Build agents are configured to skip .NET Analyzer NuGet upgrade warnings to prevent build failures during runtime updates
- **COMPLIANCE REQUIREMENT**: All code must pass Roslyn Analyzer checks without warnings or errors

<!-- AI: DO NOT MODIFY THIS SECTION -->
## StyleCop
<!-- StyleCop static analysis rules for consistent code formatting and structure -->

### REQUIRED: Indentation Standards
- **INDENTATION SIZE**: Use 4 spaces for indentation (default indentation size)
- **TAB WIDTH**: Use 4 spaces for tab width
- **SPACES ONLY**: Always use spaces instead of tabs for indentation

### MANDATORY: Spacing Rules
- **CONSISTENCY**: Follow all StyleCop spacing requirements for consistent code formatting

### REQUIRED: Readability Rules
- **TYPE ALIASES**: Use built-in type aliases (e.g., use `int` instead of `Int32`) for better readability and consistency with existing codebase

### MANDATORY: Ordering Rules
- **ELEMENT ORDER**: Order elements within a document by: kind, accessibility, constant, static, readonly
- **USING DIRECTIVES**: Always place System using directives first before other using directives
- **NAMESPACE PLACEMENT**: Place using directives inside the namespace definition
- **BLANK LINES**: Allow blank lines between using groups (optional but permitted)

### REQUIRED: Naming Rules
- **FORBIDDEN**: Never use Hungarian notation prefixes
- **TUPLE NAMES**: Use PascalCase for tuple element names
- **INFERRED TUPLES**: Do not include inferred tuple element names in analysis by default
- **NAMESPACE CONVENTIONS**: Follow namespace component naming conventions
- **LOWERCASE COMPONENTS**: Allow specific namespace components that begin with lowercase letters when configured

### REQUIRED: Maintainability Rules
- **FILE SEPARATION**: Place classes in separate files according to the class name
- **TYPE ORGANIZATION**: Each top-level type (class, interface, struct, delegate, enum) should be in its own file when it's a class
- **GROUPING ALLOWANCE**: By default, only classes require separate files; interfaces, structs, delegates, and enums can be grouped

### PREFERRED: Layout Rules
- **FILE ENDINGS**: Allow files to end with a single newline character (but not required)
- **USING STATEMENTS**: Allow consecutive using statements without braces
- **LOOP FORMATTING**: Do not place 'while' expression of a 'do'/'while' loop on the same line as the closing brace

### MANDATORY: Documentation Rules
- **PUBLIC DOCUMENTATION**: Document all publicly-exposed types and members
- **INTERNAL DOCUMENTATION**: Document all internally-exposed types and members
- **PRIVATE ELEMENTS**: Do not require documentation for private elements by default
- **INTERFACE MEMBERS**: Document all interface members regardless of accessibility
- **PRIVATE FIELDS**: Do not require documentation for private fields by default
- **COMPANY NAME**: Use "PlaceholderCompany" as the default company name in file headers (override with specific company name)
- **COPYRIGHT FORMAT**: Use standard copyright text format: "Copyright (c) {companyName}. All rights reserved."
- **REPLACEMENT VARIABLES**: Support replacement variables in copyright text using pattern `^[a-zA-Z0-9]+$`
- **XML STRUCTURE**: Wrap file headers in StyleCop-standard XML structure
- **DOCUMENTATION CULTURE**: Use "en-US" as the documentation culture
- **NAMING CONVENTION**: Use StyleCop naming convention for files (not metadata convention)
- **TAG EXCLUSIONS**: Exclude "seealso" tags from punctuation analysis in XML documentation
- **HEADER DECORATION**: Allow custom header decoration text for copyright header comments

## ADDITIONAL STYLE REQUIREMENTS
<!-- Additional coding style rules that complement StyleCop -->

### REQUIRED: Expression Simplification
- **CONDITION**: When writing expressions and declarations
- **SIMPLIFIED NEW**: Use simplified new expressions when possible
- **PARAMETER FORMATTING**: When parameters are nested, put each nested parameter on a separate line and indent the nested parameters
- **PRIVATE MEMBERS**: Use fields for private class members
- **ENFORCEMENT SCOPE**: Apply to all new code and when refactoring existing expressions
- **COMPLIANCE OUTCOME**: Cleaner code, improved readability, and consistent formatting patterns

**Implementation Examples:**
```csharp
//REQUIRED - Simplified new expressions
List<string> items = new(); // Instead of new List<string>()
OrderData order = new() { Id = 1, Name = "Test" };

//REQUIRED - Nested parameter formatting
var result = ProcessComplexData(
    orderData: new OrderData
    {
        Id = orderId,
        Name = orderName,
        Items = itemList,
    },
    customerInfo: new CustomerInfo
    {
        Name = customerName,
        Email = customerEmail,
    },
    options: defaultOptions
);

//REQUIRED - Fields for private members
public class OrderService
{
    private readonly IOrderRepository orderRepository; // Field, not property
    private readonly ILogger logger; // Field, not property

    public OrderData GetOrder(int id) => orderRepository.GetById(id);
}
```

### REQUIRED: Property and Constant Patterns
- **CONDITION**: When declaring properties and constants
- **INIT PROPERTIES**: If using `init` on a property, also use `required`
- **CONSTANTS**: Use `const` instead of `readonly static` when possible
- **NULL FORGIVING**: Never use `!.` outside of Unit Tests
- **ENFORCEMENT SCOPE**: Apply to all property declarations, constant definitions, and null handling
- **COMPLIANCE OUTCOME**: Proper initialization patterns, appropriate constant usage, and safe null handling

**Implementation Examples:**
```csharp
//REQUIRED - Init with required pattern
public class OrderData
{
    public required string Name { get; init; }
    public required int Id { get; init; }
    public required DateTime? CompletedDate { get; init; }
}

//REQUIRED - const instead of readonly static
public class OrderConstants
{
    public const int MaxOrderItems = 100; // Use const when possible
    public static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(5); // Use readonly for complex types
}

// FORBIDDEN - Null forgiving operator outside tests
public void ProcessOrder(OrderData order)
{
    var name = order.Name!.ToUpper(); // Don't use !. in production code
    // Instead, use proper null checking
    var name = order.Name?.ToUpper() ?? "Unknown";
}

//ALLOWED - Null forgiving in unit tests only
[TestMethod]
public void TestOrderProcessing()
{
    var order = CreateTestOrder();
    var result = order.Name!.ToUpper(); // Acceptable in tests when you know value is not null
}
