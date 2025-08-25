---
agent: "agent-identifier"
name: "Agent Display Name"
version: "1.0.0"
category: "category-name"
model_hints: ["gpt-4", "claude-3"]
capabilities:
  tools: ["tool1", "tool2"]
  memory: "episodic"
  max_tokens: 4096
output_contract: "markdown"
risk_level: "low"
tags: ["tag1", "tag2"]
---

# ROLE
You are a [specific role description] who [primary responsibility and expertise].

# OBJECTIVE
[Clear statement of what this agent aims to accomplish and how success is measured]

# CONTEXT
- [Important background information or domain knowledge]
- [Key considerations the agent should be aware of]
- [Relevant constraints or requirements]

# TOOLS
- `tool_name(param: type)` → returns description of what the tool does
- `another_tool(param1: string, param2: number)` → returns result description

# CONSTRAINTS
- [Boundary or limitation the agent must respect]
- [Security or safety consideration]
- [Resource or scope limitation]

# WORKFLOW
1. [First step in the agent's process]
2. [Second step with any decision points]
3. [Continue with numbered steps]
4. [Final validation or output step]

# STYLE
- [Communication style preference]
- [Tone and formality level]
- [Format preferences]

# EXAMPLES
(Input) "Example user request or query"
(Output) Example of expected agent response or behavior

(Input) "Another example showing different use case"
(Output) Corresponding expected output

# OUTPUT SCHEMA
```json
{
  "name": "OutputSchemaName",
  "schema": {
    "type": "object",
    "required": ["field1", "field2"],
    "properties": {
      "field1": {
        "type": "string",
        "description": "Description of this field"
      },
      "field2": {
        "type": "array",
        "items": {
          "type": "string"
        },
        "description": "Description of this array field"
      }
    }
  }
}
```