/**
 * Core Chat Interfaces
 *
 * This module contains only the essential interfaces needed for the
 * new handler-based chat architecture. No legacy code.
 */

// ============================================================================
// Core Types for New Architecture Only
// ============================================================================

/**
 * Streaming UI state for the chat interface
 * Used to track current streaming status across all message types
 */
export interface UIStreamingSnapshot {
	messageType: string;
	isStreaming: boolean;
	phase: 'initial' | 'streaming' | 'complete';
	// Optional deltas depending on messageType
	textDelta?: string;
	reasoningDelta?: string;
	visibility?: 'Plain' | 'Summary' | 'Encrypted' | null;
}

export interface StreamingUIState {
	isStreaming: boolean;
	currentMessageId: string | null;
	streamingSnapshots: Record<string, UIStreamingSnapshot>;
	error: string | null;
}

/**
 * Simple error event for the new architecture
 */
export interface ErrorEvent {
	chatId?: string;
	messageId?: string;
	message: string;
	kind: 'error';
}

/**
 * Simple initialization event for the new architecture
 */
export interface InitEvent {
	chatId: string;
	userMessageId: string;
	userTimestamp: string;
	userSequenceNumber: number;
}
