// Shared type definitions for chat modes functionality

/**
 * Represents a chat mode configuration that defines prompts, tools, and behavior
 */
export interface Mode {
  /** Unique identifier for the mode (e.g., "coding-assistant") */
  id: string;
  /** Display name shown in the UI */
  name: string;
  /** Brief description for UI tooltips and selection */
  description: string;
  /** System prompt text applied when mode is active */
  prompt: string;
  /** Array of tool names to enable for this mode */
  tools: string[];
  /** Optional preferred model for this mode */
  defaultModel?: string;
  /** Whether this is a system-defined or user-created mode */
  isSystem: boolean;
  /** Owner for custom modes (null for system modes) */
  userId?: string;
  /** Category for UI grouping */
  category?: ModeCategory;
  /** Creation timestamp */
  createdAt?: Date;
  /** Last update timestamp */
  updatedAt?: Date;
}

/**
 * Categories for organizing modes in the UI
 */
export type ModeCategory = 'task' | 'role' | 'custom';

/**
 * Request payload for creating a new custom mode
 */
export interface CreateModeRequest {
  name: string;
  description: string;
  prompt: string;
  tools: string[];
  defaultModel?: string;
  category?: ModeCategory;
}

/**
 * Request payload for updating an existing custom mode
 */
export interface UpdateModeRequest {
  name?: string;
  description?: string;
  prompt?: string;
  tools?: string[];
  defaultModel?: string;
  category?: ModeCategory;
}

/**
 * Response for mode operations
 */
export interface ModeResponse {
  mode: Mode;
}

/**
 * Response for listing multiple modes
 */
export interface ModesListResponse {
  modes: Mode[];
  totalCount: number;
}

/**
 * Request payload for chat operations with mode support
 */
export interface CreateChatWithModeRequest {
  message: string;
  systemPrompt?: string;
  modeId?: string;
}

/**
 * Request payload for continuing chat with mode support
 */
export interface ContinueChatWithModeRequest {
  chatId: string;
  message: string;
  modeId?: string;
}

/**
 * System mode configuration stored in JSON files
 */
export interface SystemModeConfig {
  id: string;
  name: string;
  description: string;
  category: ModeCategory;
  prompt: string;
  tools: string[];
  defaultModel?: string;
}