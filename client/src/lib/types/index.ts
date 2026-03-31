/**
 * Re-exports all core TypeScript interfaces for the Rich Message Rendering system.
 * This barrel file provides a convenient single import point for all interface types.
 */

// Core renderer interfaces
export type { MessageRenderer, CustomRenderer } from './renderer.js';

// Streaming interfaces
export type { StreamingHandler, StreamingUpdate } from './streaming.js';

// Message state interfaces
export type { MessageState, MessageStates } from '../stores/messageState.js';

// Re-export existing chat types for convenience
export type {
	Message,
	Chat,
	MessageDto,
	TextMessageDto,
	ReasoningMessageDto,
	RichMessageDto,
	ChatDto,
	CreateChatRequest,
	ChatHistoryResponse
} from './chat.js';
