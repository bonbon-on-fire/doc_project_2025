# Agent Card Format Migration Guide

## Overview

This guide explains how to migrate from JSON-based mode definitions to the new Markdown-based Agent Card format. The Agent Card format provides better readability, documentation integration, and aligns with industry standards like Model Context Protocol (MCP).

## Benefits of Migration

1. **Human-Readable**: Markdown format is easier to read and edit without tools
2. **Self-Documenting**: The specification is the documentation
3. **Version Control Friendly**: Meaningful diffs in git
4. **IDE Support**: Markdown preview and syntax highlighting
5. **Extensible**: Easy to add new sections without breaking parsers
6. **Industry Aligned**: Follows emerging patterns from MCP and structured outputs

## Migration Steps

### 1. Understand the Format Mapping

| JSON Field | Agent Card Section | Location |
|------------|-------------------|----------|
| `id` | `agent` | YAML front matter |
| `name` | `name` | YAML front matter |
| `description` | `# OBJECTIVE` | Markdown section |
| `prompt` | Multiple sections | `# ROLE`, `# CONTEXT`, etc. |
| `tools` | `capabilities.tools` | YAML front matter |
| `defaultModel` | `model_hints[0]` | YAML front matter |
| `category` | `category` | YAML front matter |

### 2. Convert JSON to Agent Card

#### Before (JSON):
```json
{
  "id": "coding",
  "name": "Coding Assistant",
  "description": "Optimized for programming tasks",
  "category": "task",
  "prompt": "You are an expert programmer...",
  "tools": ["web-search", "TaskManager"],
  "defaultModel": "openai/gpt-4"
}
```

#### After (Agent Card):
```markdown
---
agent: "coding"
name: "Coding Assistant"
version: "1.0.0"
category: "task"
model_hints: ["openai/gpt-4"]
capabilities:
  tools: ["web-search", "TaskManager"]
---

# ROLE
You are an expert programmer who assists with development tasks.

# OBJECTIVE
Optimized for programming tasks including coding, debugging, and review.

# CONTEXT
- Development best practices are important
- Code should be maintainable and tested

# WORKFLOW
1. Understand the task
2. Research if needed
3. Implement solution
4. Verify correctness
```

### 3. Expand Single Prompts

The JSON format often has a single `prompt` field. In Agent Cards, break this into structured sections:

- **ROLE**: Core identity and expertise
- **OBJECTIVE**: Primary goal
- **CONTEXT**: Background knowledge
- **TOOLS**: Available tools with descriptions
- **CONSTRAINTS**: Boundaries and limitations
- **WORKFLOW**: Step-by-step process
- **STYLE**: Communication preferences
- **EXAMPLES**: Input/output demonstrations

### 4. File Organization

```
project/
├── modes/           # Legacy JSON files (can coexist)
│   └── *.json
└── agents/          # New Agent Card files
    ├── TEMPLATE.agent.md
    ├── development/
    │   ├── senior-developer.agent.md
    │   └── code-reviewer.agent.md
    ├── research/
    │   └── research-assistant.agent.md
    └── tasks/
        ├── coding-assistant.agent.md
        └── writing-assistant.agent.md
```

### 5. Naming Convention

- Use `.agent.md` extension for agent card files
- Use kebab-case for file names: `senior-developer.agent.md`
- Match the `agent` ID in the front matter to the file name (without extension)

## Migration Script

For bulk migration, use this PowerShell script:

```powershell
# migrate-modes.ps1
param(
    [string]$SourceDir = "./modes",
    [string]$TargetDir = "./agents"
)

# Ensure target directory exists
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

Get-ChildItem -Path $SourceDir -Filter "*.json" | ForEach-Object {
    $json = Get-Content $_.FullName | ConvertFrom-Json
    
    $agentCard = @"
---
agent: "$($json.id)"
name: "$($json.name)"
version: "1.0.0"
category: "$($json.category ?? 'general')"
$(if ($json.defaultModel) { "model_hints: [`"$($json.defaultModel)`"]" })
capabilities:
  tools: [$($json.tools | ForEach-Object { "`"$_`"" } | Join-String -Separator ", ")]
---

# ROLE
$($json.prompt -split '\.' | Select-Object -First 1).

# OBJECTIVE
$($json.description)

# WORKFLOW
1. Analyze the request
2. Apply domain expertise
3. Provide solution
4. Verify quality
"@

    $targetFile = Join-Path $TargetDir "$($json.id).agent.md"
    Set-Content -Path $targetFile -Value $agentCard
    Write-Host "Migrated $($_.Name) to $targetFile"
}
```

## Validation

After migration, validate your agent cards:

1. **Parse Test**: Ensure the parser can load the file
2. **Required Fields**: Verify `agent` and `name` are present
3. **YAML Validity**: Check front matter is valid YAML
4. **Section Structure**: Confirm sections use proper markdown headers

## Migration Complete

As of the latest update:

1. **JSON support has been removed** - Only Agent Cards are supported
2. Agent Cards in `agents/` directory are the sole source of system modes
3. All system modes have been migrated to the new format
4. The `server/modes/` directory has been removed

## Testing Migration

Use the provided test to validate agent cards:

```csharp
[Fact]
public void ValidateAgentCard()
{
    var content = File.ReadAllText("path/to/agent.agent.md");
    var parser = new AgentCardParser();
    var card = parser.ParseAgentCard(content);
    
    // Validate required fields
    Assert.NotNull(card.Metadata.Agent);
    Assert.NotNull(card.Role);
    Assert.NotNull(card.Objective);
}
```

## Common Issues

### Issue: YAML Parse Errors
**Solution**: Ensure proper indentation (2 spaces) and valid YAML syntax

### Issue: Missing Sections
**Solution**: Not all sections are required. Start with ROLE and OBJECTIVE

### Issue: Tools Not Working
**Solution**: Tool names must match exactly. Check spelling and case

### Issue: JSON Schema Invalid
**Solution**: Wrap JSON in triple backticks with `json` language identifier

## Best Practices

1. **Start Simple**: Begin with minimal required fields, add sections as needed
2. **Use Examples**: Include input/output examples for clarity
3. **Document Tools**: Describe what each tool does and its parameters
4. **Version Properly**: Use semantic versioning in the `version` field
5. **Test Thoroughly**: Validate parsing and functionality after migration
6. **Preserve Intent**: Ensure the migrated agent maintains original behavior

## Support

For questions or issues with migration:
1. Review the [Agent Card Format Specification](./agent-card-format.md)
2. Check example agents in `agents/` directory
3. Run tests to validate your agent cards
4. File issues in the project repository