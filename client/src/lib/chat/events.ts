/**
 * Legacy Event Types for Backward Compatibility
 *
 * This module contains minimal legacy interfaces that may still be needed
 * during the transition to the new handler-based architecture.
 *
 * TODO: Remove this file once all components are migrated to the new architecture.
 */

import type { MessageDto } from '$lib/types/chat';

// ============================================================================
// Legacy Types (for backward compatibility only)
// ============================================================================

/**
 * @deprecated Use slimChatSyncManager with handler registry instead
 */
export interface ServerEventEnvelope {
	chatId: string;
	messageId?: string;
	sequenceId?: number;
	version: number;
	ts: string;
	kind: string;
	payload: Record<string, unknown>;
}

/**
 * Legacy initialization event
 * @deprecated Use InitEventEnvelope from sseEventTypes instead
 */
export interface InitEvent {
	chatId: string;
	userMessageId: string;
	userTimestamp: string;
	userSequenceNumber: number;
}

/**
 * Legacy error event
 * @deprecated Use ErrorEventEnvelope from sseEventTypes instead
 */
export interface ErrorEvent {
	chatId: string;
	messageId?: string;
	message: string;
	kind: 'error';
}

/**
 * Legacy streaming UI state
 * @deprecated Will be replaced with handler-specific state management
 */
export interface StreamingUIState {
	isStreaming: boolean;
	currentMessageId: string | null;
	currentTextDelta: string;
	currentReasoningDelta: string;
	currentReasoningVisibility: string | null;
	error: string | null;
}

/**
 * Legacy SSE event parser interface
 * @deprecated Use strongly-typed SSE parser instead
 */
export interface SSEParser {
	parseSSEData(data: string): { eventType: string; eventData: string } | null;
	parseEventEnvelope(eventData: string): ServerEventEnvelope | null;
}
