# Chat Modes User Guide

## Overview

Chat Modes allow you to customize your AI assistant's behavior and capabilities for specific tasks. Each mode configures the AI with a specialized system prompt and a curated set of tools, optimizing the assistant for different types of work.

## Using Chat Modes

### Selecting a Mode

When starting a new conversation:

1. **Welcome Screen**: In the main chat area, you'll see a mode selector dropdown at the top of the welcome message
2. **Choose Your Mode**: Click the dropdown to see all available modes, organized by category
3. **View Mode Details**: Hover over any mode to see:
   - A description of what the mode is optimized for
   - The number of tools available in that mode
   - Tool availability indicators (green checkmark = available, yellow warning = missing)

### Mode Categories

Modes are organized into logical categories for easy navigation:

- **Task**: Modes optimized for specific types of work (coding, writing, research)
- **Role**: Modes that give the AI a specific professional role
- **Custom**: Your personal custom-created modes
- **System**: Built-in system modes

### Switching Modes During a Conversation

You can change modes at any time during an active conversation:

1. Look for the mode selector in the chat header
2. Select a new mode from the dropdown
3. A toast notification will confirm the mode change
4. The AI will adapt to the new mode's configuration for subsequent messages

**Note**: Switching modes mid-conversation maintains the conversation context while applying the new mode's tools and behavior.

## Available System Modes

### General Assistant
- **ID**: `general`
- **Category**: Task
- **Description**: Default mode with all available tools
- **Tools**: All available tools ("*")
- **Best For**: General queries, mixed tasks, exploration
- **Behavior**: Helpful AI assistant with access to the full range of capabilities

### Coding Assistant
- **ID**: `coding`
- **Category**: Task
- **Description**: Optimized for programming tasks with development tools
- **Tools**: Web search, webpage fetching, task management
- **Default Model**: OpenAI GPT-4
- **Best For**: 
  - Writing and debugging code
  - Code reviews and refactoring
  - Technical problem-solving
  - Development workflow management
- **Behavior**: Expert programming assistant focused on software development

### Writing Assistant
- **ID**: `writing`
- **Category**: Task
- **Description**: Optimized for content creation and writing tasks
- **Tools**: Web search, webpage fetching
- **Default Model**: Claude-3
- **Best For**:
  - Content creation and editing
  - Proofreading and grammar checking
  - Creative writing
  - Research for articles
- **Behavior**: Skilled writer and editor focused on clear, engaging communication

### Research Assistant
- **ID**: `research`
- **Category**: Task
- **Description**: Specialized for information gathering and analysis
- **Tools**: Web search, webpage fetching
- **Best For**:
  - Fact-checking and verification
  - Data analysis and synthesis
  - Comprehensive research projects
  - Information gathering from multiple sources
- **Behavior**: Research specialist focused on thorough, accurate information gathering

## Creating Custom Modes

Custom modes allow you to tailor the AI assistant to your specific needs and workflows.

### What You Can Customize

1. **Name**: A descriptive name for your mode
2. **Description**: What the mode is optimized for
3. **Category**: How to organize your mode (task, role, or custom)
4. **System Prompt**: Instructions that shape the AI's behavior and responses
5. **Tools**: Select specific tools the AI can use
6. **Default Model**: Optionally specify a preferred AI model

### Creating a Custom Mode via API

Currently, custom modes are created through the API. Here's what you need to know:

#### Mode Configuration Structure

```json
{
  "name": "My Custom Mode",
  "description": "A mode tailored for my specific workflow",
  "category": "custom",
  "prompt": "You are an AI assistant specialized in...",
  "tools": ["web-search", "TaskManager"],
  "defaultModel": "openai/gpt-4"
}
```

#### Field Guidelines

**Name** (required):
- Keep it concise and descriptive
- Avoid special characters (<, >, &)
- Maximum 100 characters

**Description** (required):
- Clearly explain what the mode is for
- Help users understand when to use this mode
- Maximum 500 characters

**Category** (optional):
- Use "task" for work-focused modes
- Use "role" for personality-based modes
- Defaults to "custom" if not specified

**Prompt** (required):
- Define the AI's behavior and expertise
- Include specific instructions or constraints
- Can reference the mode's intended use case
- Maximum 2000 characters

**Tools** (required):
- List specific tool IDs to include
- Use ["*"] to include all available tools
- Empty array [] means no tools
- Common tool IDs:
  - `web-search`: Web search capability
  - `webpage-fetch`: Fetch and analyze web pages
  - `TaskManager`: Task and project management

**DefaultModel** (optional):
- Specify a preferred AI model
- Examples: "openai/gpt-4", "claude-3", "gemini-pro"
- Leave null to use the system default

### Best Practices for Custom Modes

1. **Be Specific**: Create modes for specific workflows rather than general purposes
2. **Choose Tools Wisely**: Only include tools relevant to the mode's purpose
3. **Write Clear Prompts**: Make the AI's role and constraints explicit
4. **Test and Iterate**: Try your mode and refine based on results
5. **Name Descriptively**: Use names that immediately convey the mode's purpose

### Example Custom Modes

#### Technical Documentation Mode
```json
{
  "name": "Technical Writer",
  "description": "Creates clear, structured technical documentation",
  "category": "role",
  "prompt": "You are a technical documentation specialist. Focus on clarity, accuracy, and structure. Use examples and diagrams where helpful.",
  "tools": ["web-search"],
  "defaultModel": "openai/gpt-4"
}
```

#### Data Analysis Mode
```json
{
  "name": "Data Analyst",
  "description": "Analyzes data and provides insights",
  "category": "role",
  "prompt": "You are a data analyst. Focus on identifying patterns, trends, and actionable insights. Present findings clearly with supporting evidence.",
  "tools": ["web-search", "TaskManager"],
  "defaultModel": null
}
```

## Mode Management

### Viewing Your Modes

All your available modes (system and custom) appear in the mode selector dropdown, organized by category for easy access.

### Editing Custom Modes

Custom modes can be updated via the API. Changes take effect immediately for new conversations.

### Deleting Custom Modes

Custom modes can be deleted via the API. System modes cannot be deleted.

## Understanding Tool Availability

### Tool Indicators

When viewing modes, you'll see indicators showing tool availability:

- ✅ **Green Checkmark**: Tool is available and ready
- ⚠️ **Yellow Warning**: Tool is configured but not currently available
- **Tool Count**: Shows total available tools for the mode

### Handling Missing Tools

If a mode references tools that aren't available:

1. The mode will still function with available tools
2. A warning indicator appears in the mode selector
3. Tooltips show which specific tools are missing
4. The AI adapts gracefully to work without missing tools

## Tips for Effective Mode Usage

### Choosing the Right Mode

1. **Start Specific**: Choose a mode that matches your immediate task
2. **Switch as Needed**: Don't hesitate to change modes as your needs evolve
3. **Use General for Exploration**: When unsure, the General mode provides full capabilities

### Mode Performance

- **Focused Modes**: Specialized modes often provide better results for their specific domain
- **Tool Optimization**: Modes with fewer, relevant tools can be more efficient
- **Model Selection**: Some modes specify optimal AI models for their use case

### Common Workflows

#### Research Project
1. Start with **Research Assistant** for information gathering
2. Switch to **Writing Assistant** for report creation
3. Use **General Assistant** for final review and polish

#### Software Development
1. Use **Coding Assistant** for implementation
2. Switch to **Writing Assistant** for documentation
3. Return to **Coding Assistant** for debugging

#### Content Creation
1. Begin with **Research Assistant** for background information
2. Switch to **Writing Assistant** for drafting
3. Use **General Assistant** for fact-checking and refinement

## Troubleshooting

### Mode Not Available
- Ensure you're logged in with proper permissions
- Check for any system notifications about mode availability
- Try refreshing the page

### Tools Not Working
- Yellow warning indicators show which tools are unavailable
- The mode will still function with available tools
- Contact support if critical tools are consistently unavailable

### Mode Changes Not Taking Effect
- Mode changes apply to new messages only
- Existing messages retain their original mode context
- Try starting a new conversation if issues persist

## Future Enhancements

The modes feature is continuously evolving. Upcoming features may include:

- Visual mode editor interface
- Mode templates and sharing
- Advanced tool configuration
- Mode performance analytics
- Collaborative mode creation

## Getting Help

If you need assistance with modes:

1. Check this guide for common questions
2. Review mode descriptions and tooltips in the UI
3. Experiment with different modes to understand their behavior
4. Contact support for technical issues or feature requests