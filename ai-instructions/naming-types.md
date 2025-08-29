# NAMING AND TYPE DECLARATIONS
<!-- Variable naming, type usage, and declaration standards -->

## CONDITIONAL: var Keyword Usage
- **CONDITION**: When declaring variables and choosing between var and explicit types
- **USE var WHEN**: Type is obvious from right side of assignment
- **AVOID var WHEN**: Type is not clear or would reduce code readability
- **ENFORCEMENT SCOPE**: Apply to all variable declarations in new and modified code
- **COMPLIANCE OUTCOME**: Improved code readability and consistent variable declaration patterns

**Implementation Examples:**
```csharp
//Good - type is obvious
var list = new List<string>();
var conversations = GetActiveConversations();

//Good - type not obvious from method name
string result = GetComplexResult();
ConversationData data = ProcessConversationInformation();
```

## REQUIRED: Records Usage
- **CONDITION**: When creating data structures and choosing between record and class types
- **DTOs/DATA MODELS**: Use `record class` for data transfer objects and data models
- **OTHER CLASSES**: Use regular `class` for behavior-focused types
- **PREFERRED**: Positional record classes over traditional property syntax
- **PROPERTIES**: Use properties instead of fields in record classes
- **ENFORCEMENT SCOPE**: Apply to all new type declarations and when refactoring existing types
- **COMPLIANCE OUTCOME**: Improved immutability, cleaner syntax, and appropriate type usage

**Implementation Examples:**
```csharp
//REQUIRED - Record class for data models
public record class ConversationData(string Id, string? Title, DateTimeOffset CreatedAt);

//REQUIRED - Regular class for behavior-focused types
public class ConversationService
{
    public ConversationData ProcessConversation(ConversationRequest request) { ... }
}

//PREFERRED - Positional record syntax
public record class MessageInfo(string Content, Role Role, DateTimeOffset Timestamp);

// AVOID - Fields in record classes
public record class ConversationData
{
    public string Id; // Use property instead
}
```

<!-- AI: Do not modify order within this section -->
## MANDATORY: Naming Conventions
- **Static Readonly Fields**: PascalCase (e.g., `DefaultTimeout`)
- **Public Fields**: PascalCase (e.g., `UserName`)
- **Constants**: PascalCase (e.g., `MaxRetryCount`)
- **Parameters**: camelCase (e.g., `userName`, `itemCount`)
- **Properties**: PascalCase (e.g., `UserName`, `ItemCount`)
- **Fields**: camelCase without underscore prefix (e.g., `userName`, `itemCount`)
- **Private Fields**: camelCase without underscore (e.g., `userName`)
- **FORBIDDEN**: Private static properties (use fields instead)
- **REQUIREMENT**: Use meaningful names for all variables, methods, and classes

## REQUIRED: Properties
- **CONDITION**: When declaring class members that expose data or state
- **PUBLIC MEMBERS**: Use properties instead of fields
- **CONSTRUCTOR-ONLY**: Omit setters for constructor-only properties unless using `required`
- **INIT PATTERN**: If using `init`, also use `required` keyword
- **ENFORCEMENT SCOPE**: Apply to all public member declarations and property definitions
- **COMPLIANCE OUTCOME**: Improved encapsulation, consistent API design, and proper initialization patterns

**Implementation Examples:**
```csharp
//REQUIRED - Properties for public members
public class OrderData
{
    public int Id { get; }
    public string Name { get; }
    public DateTime CreatedDate { get; }
}

//REQUIRED - Init with required pattern
public class UserProfile
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string? Phone { get; init; }
}

// AVOID - Public fields
public class OrderData
{
    public int Id; // Use property instead
    public string Name; // Use property instead
}
```

## REQUIRED: Method and Constructor Usage
- **CONDITION**: When calling methods or constructors with parameters
- **ARGUMENT VALIDATION**: Always ensure that arguments passed to methods and constructors match the expected type and order by reviewing the method signature carefully, instead of relying solely on build errors to catch mistakes
- **NAMED ARGUMENTS**: Use named arguments when calling methods or constructors that have multiple parameters of the same type, many optional parameters, or when the argument order is not immediately obvious
- **TRAILING COMMAS**: Always use trailing commas in multiline initializers
- **INDENTATION**: Always use spaces instead of tabs for indentation
- **ENFORCEMENT SCOPE**: Apply to all method calls, constructor calls, and object initialization
- **COMPLIANCE OUTCOME**: Reduced runtime errors, improved code clarity, and consistent formatting

**Implementation Examples:**
```csharp
//REQUIRED - Named arguments for clarity
var order = CreateOrder(
    orderId: 123,
    customerName: "John Doe",
    priority: OrderPriority.High,
    requiresApproval: true
);

//REQUIRED - Trailing commas in multiline initializers
var orderData = new OrderData
{
    Id = 1,
    Name = "Test Order",
    Items = items,
    CreatedDate = DateTime.UtcNow, // Trailing comma required
};

//REQUIRED - Argument validation before calling
public void ProcessOrder(OrderData order, CustomerInfo customer)
{
    // Verify method signature matches before calling
    var result = orderProcessor.Process(
        order: order,        // Matches ProcessorOrder parameter
        customer: customer,  // Matches CustomerInfo parameter
        options: defaultOptions
    );
}
```

## REQUIRED: Test Code Quality
- **CONDITION**: When writing or maintaining unit tests and test utilities
- **DEAD CODE AVOIDANCE**: Avoid generating or keeping test methods that are not invoked or referenced anywhere to reduce dead code and improve code clarity
- **UTILITY REUSE**: Do not duplicate logic that already exists in helpers or utility files. For example, prefer reusing existing utilities like `TestDataGenerator` (and others already present in the repo) wherever applicable instead of creating new ad hoc test data setups or helper methods
- **UTILITY EXTENSION**: Extend existing test utility classes and helpers with new methods if the logic is general-purpose and reusable across tests. For example, enhance utilities like `TestDataGenerator` when adding new reusable test data patterns, rather than duplicating the setup in individual test files
- **ENFORCEMENT SCOPE**: Apply to all test code creation and maintenance activities
- **COMPLIANCE OUTCOME**: Reduced code duplication, improved maintainability, and cleaner test suites

**Implementation Examples:**
```csharp
//REQUIRED - Reuse existing test utilities
[TestMethod]
public void ProcessConversation_ValidData_ReturnsSuccess()
{
    // Reuse existing utility instead of creating new test data
    var testConversation = TestDataGenerator.CreateValidConversation();
    var result = conversationService.ProcessConversation(testConversation);
    Assert.IsTrue(result);
}

//REQUIRED - Extend existing utilities when needed
public static class TestDataGenerator
{
    // Add new method to existing utility
    public static ConversationData CreateConversationWithMessages(int messageCount)
    {
        return new ConversationData("1", "Test Conversation", CreateMessages(messageCount));
    }
}

// AVOID - Duplicating existing utility logic
[TestMethod]
public void TestMethod()
{
    // Don't recreate logic that exists in TestDataGenerator
    var conversation = new ConversationData { Id = "1", Title = "Test" }; // Use utility instead
}
```

## REQUIRED: Property and Member Access
- **VALIDATION**: Never reference properties, fields, or methods that do not exist in a class. Always validate the class definition before using its members to avoid runtime errors
- **ENFORCEMENT SCOPE**: Apply when accessing any class member in new or modified code
- **COMPLIANCE OUTCOME**: Prevents compilation errors and runtime exceptions due to missing members

**Implementation Examples:**
```csharp
//REQUIRED - Verify member exists before use
public class ConversationService
{
    public void ProcessConversation(Conversation conversation)
    {
        // Ensure Conversation class has Title property before accessing
        if (!string.IsNullOrEmpty(conversation.Title))
        {
            ProcessConversationTitle(conversation.Title);
        }
    }
}

// FORBIDDEN - Accessing non-existent members
public void ProcessConversation(Conversation conversation)
{
    // Don't assume properties exist without verification
    var result = conversation.NonExistentProperty; // Will cause compilation error
}
