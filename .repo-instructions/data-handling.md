# DATA HANDLING
<!-- Data structure and time handling standards -->

## REQUIRED: Immutability
- **CONDITION**: When working with collections and data structures
- **PREFER**: ImmutableList<T>, ImmutableArray<T>, ImmutableDictionary<K,V>
- **CONDITIONAL**: Use mutable collections only when performance is critical
- **ENFORCEMENT SCOPE**: Apply to all new data structure choices and collection usage
- **COMPLIANCE OUTCOME**: Prevents unintended side effects and improves thread safety

**Implementation Examples:**
```csharp
//REQUIRED - Immutable collections
public ImmutableList<ConversationSummary> GetActiveConversations()
{
    return conversations.Where(c => c.Status == ConversationStatus.Active).ToImmutableList();
}

// CONDITIONAL - Mutable collections only when performance critical
private List<CachedMessage> highPerformanceCache = new(); // Performance-critical scenario
```

## REQUIRED: DateTime Usage Policy
- **STRICT PROHIBITION**: Never add new instances of `DateTime.Now`, `DateTime.UtcNow`, or any `SystemTime` class usage in any new code
- **LEGACY EXEMPTION**: Existing `DateTime.Now`/`DateTime.UtcNow` references may remain until scheduled for refactoring - do not modify unless specifically updating that functionality
- **SYSTEMTIME BAN**: `SystemTime` class and its members are completely forbidden in all code (new and existing)
- **MANDATORY INJECTION**: All new time-dependent functionality must use `TimeProvider` dependency injection
- **CONSTRUCTOR REQUIREMENT**: Any new class needing current time must accept `TimeProvider` in constructor
- **TESTING BENEFIT**: TimeProvider enables testable time-dependent code with mock implementations and FakeTimeProvider
- **ENFORCEMENT SCOPE**: Apply when writing new methods, classes, or features that require current time
- **COMPLIANCE OUTCOME**: Improved testability, consistent time handling, and controlled time in unit tests

**Implementation Examples:**
```csharp
// REQUIRED - New code pattern
public class ConversationService
{
    private readonly TimeProvider timeProvider;

    public ConversationService(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public Conversation CreateConversation() => new Conversation { CreatedAt = this.timeProvider.GetUtcNow() };
}

// FORBIDDEN - New DateTime usage (will be flagged in code review)
public Conversation CreateConversation() => new Conversation { CreatedAt = DateTime.UtcNow };

// STRICTLY FORBIDDEN - SystemTime usage (immediate rejection)
public Conversation CreateConversation() => new Conversation { CreatedAt = SystemTime.UtcNow };

// LEGACY EXEMPTION - Existing code remains unchanged unless refactoring
// DateTime.Now usage here is temporarily acceptable in legacy components
