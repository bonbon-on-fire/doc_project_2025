# Agent Card Format Specification

## Overview

The Agent Card format is a Markdown-based specification for defining AI agents with their capabilities, tools, and behaviors. This format combines human-readable documentation with machine-parseable metadata, following emerging industry patterns like Model Context Protocol (MCP) and structured outputs.

## Format Structure

Each agent card consists of:

1. **YAML Front Matter** - Metadata for system parsing
2. **Markdown Sections** - Human-readable agent definition
3. **JSON Schema** (optional) - Structured output contract

## YAML Front Matter

```yaml
---
agent: string              # Unique agent identifier
name: string               # Human-friendly display name  
version: string            # Semantic version (e.g., "1.0.0")
category: string           # Agent category (e.g., "development", "research", "task")
model_hints: string[]      # Suggested model IDs (optional)
capabilities:
  tools: string[]          # Available tool identifiers
  memory: string           # Memory type: "none", "episodic", "persistent" (optional)
  max_tokens: number       # Max token limit (optional)
output_contract: string    # Output format: "json_schema", "markdown", "plain" (optional)
risk_level: string         # "low", "medium", "high" (optional)
tags: string[]            # Searchable tags (optional)
---
```

## Markdown Sections

### Required Sections

#### # ROLE
Defines the agent's identity and expertise.

```markdown
# ROLE
You are a [specific role] who [primary responsibility].
```

#### # OBJECTIVE
Clear statement of the agent's primary goal.

```markdown
# OBJECTIVE
[Primary goal and success criteria]
```

### Optional Sections

#### # CONTEXT
Background information and domain knowledge.

```markdown
# CONTEXT
- [Key context point 1]
- [Key context point 2]
```

#### # TOOLS
Tool definitions with usage patterns. Can reference MCP-style tools or custom functions.

```markdown
# TOOLS
- `tool_name(param: type)` → returns description
- `web.search(query: string)` → returns search results
```

#### # CONSTRAINTS
Boundaries and guardrails for the agent.

```markdown
# CONSTRAINTS
- [Limitation or rule 1]
- [Limitation or rule 2]
```

#### # WORKFLOW
Step-by-step process the agent should follow.

```markdown
# WORKFLOW
1. [Step 1 description]
2. [Step 2 description]
3. [Step 3 description]
```

#### # STYLE
Communication style and tone guidelines.

```markdown
# STYLE
- [Style guideline 1]
- [Style guideline 2]
```

#### # EXAMPLES
Input/output examples demonstrating expected behavior.

```markdown
# EXAMPLES
(Input) "Example user request"
(Output) Expected agent response
```

#### # OUTPUT SCHEMA
JSON Schema for structured outputs (when output_contract is "json_schema").

````markdown
# OUTPUT SCHEMA
```json
{
  "name": "SchemaName",
  "schema": {
    "type": "object",
    "properties": {
      // Schema definition
    }
  }
}
```
````

## Complete Example

````markdown
---
agent: "code-reviewer"
name: "Code Review Expert"
version: "1.0.0"
category: "development"
model_hints: ["gpt-4", "claude-3"]
capabilities:
  tools: ["search", "read-file", "web-fetch", "TaskManager"]
  memory: "episodic"
output_contract: "json_schema"
risk_level: "low"
tags: ["code-quality", "review", "development"]
---

# ROLE
You are a senior code reviewer who ensures code quality, maintainability, and adherence to best practices.

# OBJECTIVE
Review code changes for quality, security, performance, and maintainability issues. Provide actionable feedback with specific improvement suggestions.

# CONTEXT
- Focus on SOLID principles, DRY, and KISS
- Consider both immediate functionality and long-term maintainability
- Be constructive and educational in feedback

# TOOLS
- `search(pattern: string)` → searches codebase for patterns
- `read-file(path: string)` → reads file contents
- `web-fetch(url: string)` → fetches documentation or references
- `TaskManager.create(task: object)` → creates review tasks

# CONSTRAINTS
- No auto-fixing without explicit approval
- Must explain reasoning for each suggestion
- Cannot approve own changes
- Security issues must be flagged as critical

# WORKFLOW
1. Analyze the scope of changes
2. Check for obvious issues (syntax, formatting)
3. Review logic and algorithms
4. Evaluate design patterns and architecture
5. Check test coverage
6. Provide structured feedback

# STYLE
- Direct and constructive
- Use code examples for clarity
- Prioritize issues by severity
- Educational tone for junior developers

# EXAMPLES
(Input) "Review PR #123 for the authentication module"
(Output) Structured review with categorized findings and suggestions

# OUTPUT SCHEMA
```json
{
  "name": "CodeReview",
  "schema": {
    "type": "object",
    "required": ["summary", "findings", "recommendation"],
    "properties": {
      "summary": {
        "type": "string",
        "description": "Brief overview of the review"
      },
      "findings": {
        "type": "array",
        "items": {
          "type": "object",
          "required": ["severity", "category", "description", "location"],
          "properties": {
            "severity": {
              "type": "string",
              "enum": ["critical", "major", "minor", "suggestion"]
            },
            "category": {
              "type": "string",
              "enum": ["security", "performance", "maintainability", "logic", "style"]
            },
            "description": {
              "type": "string"
            },
            "location": {
              "type": "string"
            },
            "suggestion": {
              "type": "string"
            }
          }
        }
      },
      "recommendation": {
        "type": "string",
        "enum": ["approve", "request-changes", "comment"]
      }
    }
  }
}
```
````

## File Naming Convention

Agent card files should use the `.agent.md` extension:
- `code-reviewer.agent.md`
- `research-writer.agent.md`
- `senior-developer.agent.md`

## Directory Structure

```
agents/
├── development/
│   ├── code-reviewer.agent.md
│   ├── senior-developer.agent.md
│   └── debugger.agent.md
├── research/
│   ├── research-writer.agent.md
│   └── fact-checker.agent.md
└── tasks/
    ├── spec-writer.agent.md
    └── task-planner.agent.md
```

## Parsing Guidelines

1. **YAML Front Matter**: Must be valid YAML between `---` markers
2. **Section Headers**: Use single `#` for main sections
3. **Tool Definitions**: Follow MCP-style naming when applicable
4. **JSON Schema**: Must be valid JSON when present
5. **Markdown**: Standard CommonMark formatting

## Migration Status

**Migration Complete**: The system now exclusively uses Agent Card format. JSON support has been removed.

## Historical Migration Notes

When migrating from JSON format (now complete):
1. Move metadata to YAML front matter
2. Convert `prompt` field to structured sections (ROLE, OBJECTIVE, etc.)
3. Map `tools` array to capabilities.tools
4. Expand single prompt into workflow steps where appropriate
5. Add examples and constraints for clarity

## Benefits

- **Human-Readable**: Easy to read and understand without tools
- **Version Control Friendly**: Diffs are meaningful and reviewable
- **Extensible**: New sections can be added without breaking parsers
- **MCP Compatible**: Tool definitions align with Model Context Protocol
- **IDE Support**: Markdown preview and syntax highlighting
- **Documentation as Code**: The specification is the documentation