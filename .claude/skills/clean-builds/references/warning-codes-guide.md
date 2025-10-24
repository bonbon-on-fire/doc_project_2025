# Clean Builds: Warning Codes Reference Guide

This guide provides detailed information on common warning codes and how to fix them.

## Code Organization Warnings

### IDE0005: Remove unnecessary imports (unused using statements)

**What it means:** A `using` statement is declared but not used in the file.

**Example of bad code:**
```csharp
using System;
using System.Collections.Generic;  // Not used
using System.Linq;

namespace MyApp
{
    public class MyClass
    {
        public void DoSomething()
        {
            var items = new List<int>();
            Console.WriteLine(items.Count);
        }
    }
}
```

**Fix:**
```csharp
using System;
using System.Collections.Generic;

namespace MyApp
{
    public class MyClass
    {
        public void DoSomething()
        {
            var items = new List<int>();
            Console.WriteLine(items.Count);
        }
    }
}
```

**Why this matters:** Unused imports slow down IntelliSense, confuse developers about dependencies, and add unnecessary clutter.

**How to enable detection:**
To have IDE0005 warnings reported during build, add this to your `.csproj` PropertyGroup:
```xml
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

This enables all style rule analysis during compilation, including IDE0005.

**Auto-fix:**
The `dotnet format` command automatically removes unused imports when you run the formatting script.

---

### IDE0017: Use inline variable declaration

**What it means:** A variable is declared separately from its initialization.

**Example of bad code:**
```csharp
int x;
x = 5;
```

**Fix:**
```csharp
int x = 5;
```

**Why this matters:** Combining declaration and initialization makes code more concise and reduces the scope where the variable is uninitialized.

---

### IDE0025: Use expression body for properties

**What it means:** A simple property getter can be simplified to use an expression body.

**Example of bad code:**
```csharp
public string Name
{
    get { return _name; }
}
```

**Fix:**
```csharp
public string Name => _name;
```

**Why this matters:** Expression bodies are more concise and modern C# style.

---

### IDE0028: Use collection initializers

**What it means:** Collections can be initialized more concisely.

**Example of bad code:**
```csharp
var list = new List<int>();
list.Add(1);
list.Add(2);
list.Add(3);
```

**Fix:**
```csharp
var list = new List<int> { 1, 2, 3 };
```

**Why this matters:** Collection initializers are more readable and less error-prone.

---

### IDE0052: Remove unread private member

**What it means:** A private field, property, or method is declared but never used.

**Example of bad code:**
```csharp
private string _unused = "This is never read";
```

**Fix:**
Remove the field entirely, or if it will be used later, add a comment explaining why it exists.

**Why this matters:** Dead code adds unnecessary complexity and maintenance burden. If something should be kept for future use, it should be documented.

---

### IDE0032: Use auto property

**What it means:** A property only has a simple getter and setter that don't do anything special.

**Example of bad code:**
```csharp
private string _name;
public string Name
{
    get { return _name; }
    set { _name = value; }
}
```

**Fix:**
```csharp
public string Name { get; set; }
```

**Why this matters:** Auto-properties reduce boilerplate and are the modern way to write simple properties in C#.

---

## Performance Warnings

### CA1826: Use property instead of LINQ

**What it means:** A LINQ method like `.FirstOrDefault()` is being used when a more direct property exists.

**Example of bad code:**
```csharp
var first = list.Where(x => x.IsActive).FirstOrDefault();
```

**Better fix:**
```csharp
var first = list.FirstOrDefault(x => x.IsActive);
```

**Why this matters:** The direct method is more efficient (doesn't create intermediate collections) and clearer.

---

### CA1859: Use concrete types when possible for better performance

**What it means:** A variable is typed as an interface when a concrete type would be better.

**Example of bad code:**
```csharp
IList<string> names = new List<string>();
```

**Fix:**
```csharp
List<string> names = new List<string>();
```

Or if you need the interface semantics:
```csharp
IList<string> names = new List<string>();  // Keep interface only if needed for contracts
```

**Why this matters:** Concrete types can be inlined by the JIT compiler, leading to better performance. Use interfaces only when you need polymorphism or have multiple implementations.

---

### CA1860: Avoid using 'Enumerable.Any()' extension method

**What it means:** For checking if a collection is empty, there are better alternatives.

**Example of bad code:**
```csharp
if (items.Any())
{
    // Do something
}
```

**Fix (if items is ICollection):**
```csharp
if (items.Count > 0)
{
    // Do something
}
```

**Why this matters:** Direct property access is faster than LINQ enumeration.

---

## Correctness Warnings

### CA1310: Specify StringComparison

**What it means:** String comparisons should explicitly specify cultural rules.

**Example of bad code:**
```csharp
if (name == "Admin")
{
    // ...
}
```

**Fix:**
```csharp
if (name.Equals("Admin", StringComparison.Ordinal))
{
    // ...
}
```

Or for case-insensitive comparisons:
```csharp
if (name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
{
    // ...
}
```

**Why this matters:** Without explicit comparison rules, different cultures can produce different results, leading to hard-to-debug bugs.

---

### CA1304: Specify CultureInfo

**What it means:** Methods that accept CultureInfo should specify it explicitly.

**Example of bad code:**
```csharp
string upper = text.ToUpper();  // Uses current culture
```

**Fix:**
```csharp
string upper = text.ToUpper(CultureInfo.InvariantCulture);
```

**Why this matters:** Using the current culture can cause different behavior on different machines.

---

### CA1305: Specify IFormatProvider

**What it means:** Methods that format values should specify an IFormatProvider.

**Example of bad code:**
```csharp
string formatted = value.ToString();
```

**Fix:**
```csharp
string formatted = value.ToString(CultureInfo.InvariantCulture);
```

**Why this matters:** Explicit format providers ensure consistent behavior across different locales.

---

## Modern Pattern Warnings

### CA1513: Use 'nameof' instead of hardcoded string

**What it means:** Hardcoded string names should use the `nameof()` operator.

**Example of bad code:**
```csharp
public int Age
{
    get => _age;
    set
    {
        if (value < 0)
            throw new ArgumentException("Age");  // Hardcoded string
    }
}
```

**Fix:**
```csharp
public int Age
{
    get => _age;
    set
    {
        if (value < 0)
            throw new ArgumentException(nameof(Age));  // Uses nameof
    }
}
```

**Why this matters:** If you rename the property, `nameof()` automatically updates, while hardcoded strings don't.

---

## Workflow for Fixing Warnings

1. **Get the warning details**
   - Note the code (e.g., CA1826, IDE0052)
   - Note the file and line number
   - Read the message for context

2. **Look up the code in this guide** (or search online)
   - Understand what the issue is
   - Read the example and fix

3. **Apply the fix**
   - Make the change to your code
   - Consider whether similar patterns exist elsewhere

4. **Re-run the build check**
   ```pwsh
   pwsh scripts/build_and_group_errors_and_warnings.ps1
   ```

5. **Verify** the warning is gone

## Tips for Bulk Warning Fixes

If you have many warnings of the same code:

1. Use your IDE's "Find All References" or search/replace
2. Apply the fix in one place
3. Use "Replace All" if the pattern is consistent
4. Run the build check again to verify

## When NOT to Fix a Warning

Some warnings may not apply to your specific use case. You can suppress them:

```csharp
#pragma warning disable CA1859  // Use concrete types
IEnumerable<int> items = new List<int>();
#pragma warning restore CA1859
```

Only suppress warnings if you have a documented reason. Document in a comment why it's suppressed.

## Links and Resources

- [Microsoft Analyzer Rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- [C# Best Practices](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [CA Rules Index](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)
