/**
 * SSE Parser Implementation
 *
 * Handles the low-level parsing of Server-Sent Events according to the SSE specification.
 * Separates protocol concerns from business logic and provides type-safe parsing
 * of server SSE event envelopes.
 */

import type { SSEParser, ServerEventEnvelope } from './events';
import type {
	SSEEventEnvelope,
	SSEEventEnvelopeUnion,
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope,
	ErrorEventEnvelope,
	SSEEventGuards
} from './sseEventTypes';

/**
 * Implementation of SSEParser for handling raw SSE data
 *
 * Single Responsibility: Only responsible for SSE protocol parsing
 * Interface Segregation: Focused on SSE parsing operations only
 */
export class SSEParserImpl implements SSEParser {
	/**
	 * Parse raw SSE data into structured events
	 *
	 * Handles the SSE format:
	 * event: eventType
	 * data: eventData
	 * id: eventId (optional)
	 */
	parseSSEData(data: string): { eventType: string; eventData: string } | null {
		try {
			const lines = data.split('\n');
			let eventType = '';
			let eventData = '';

			for (const line of lines) {
				if (line.startsWith('event:')) {
					eventType = line.substring(6).trim();
				} else if (line.startsWith('data:')) {
					eventData = line.substring(5).trim();
				}
				// Note: We ignore 'id:' lines as they're handled by the browser
			}

			if (eventType && eventData) {
				return { eventType, eventData };
			}

			return null;
		} catch (error) {
			console.error('Error parsing SSE data:', error, { data });
			return null;
		}
	}

	/**
	 * Parse event data JSON into strongly-typed envelope
	 * Returns the appropriate SSE event envelope type based on event kind
	 */
	parseEventEnvelope(eventData: string): ServerEventEnvelope | null {
		// For backward compatibility, use the legacy method
		return this.parseEventEnvelopeLegacy(eventData);
	}

	/**
	 * Parse event data JSON into strongly-typed SSE envelope
	 * Returns the appropriate SSE event envelope type based on event kind
	 */
	/**
	 * Parse event data JSON into strongly-typed SSE envelope
	 * Returns the appropriate SSE event envelope type based on event kind
	 */
	parseTypedEventEnvelope(eventData: string): SSEEventEnvelopeUnion | null {
		try {
			const parsed = JSON.parse(eventData);

			// Validate required fields
			if (!parsed || typeof parsed !== 'object') {
				console.warn('Invalid event envelope: not an object', { eventData });
				return null;
			}

			// chatId and kind are required for all events
			if (!parsed.chatId || !parsed.kind) {
				console.warn('Invalid event envelope: missing required fields', { parsed });
				return null;
			}

			// Parse based on event kind for type safety
			const baseEnvelope: SSEEventEnvelope = {
				chatId: String(parsed.chatId),
				version: Number(parsed.version || 1),
				ts: String(parsed.ts || new Date().toISOString()),
				kind: String(parsed.kind)
			};

			switch (parsed.kind) {
				case 'meta': {
					if (!parsed.payload || !parsed.payload.userMessageId) {
						console.warn('Invalid init event: missing payload or userMessageId', { parsed });
						return null;
					}

					const initEnvelope: InitEventEnvelope = {
						...baseEnvelope,
						kind: 'meta',
						payload: {
							userMessageId: String(parsed.payload.userMessageId),
							userTimestamp: String(parsed.payload.userTimestamp),
							userSequenceNumber: Number(parsed.payload.userSequenceNumber || 0)
						}
					};
					return initEnvelope;
				}

				case 'complete': {
					const completeEnvelope: StreamCompleteEventEnvelope = {
						...baseEnvelope,
						kind: 'complete'
					};
					return completeEnvelope;
				}

				case 'error': {
					if (!parsed.payload || !parsed.payload.message) {
						console.warn('Invalid error event: missing payload or message', { parsed });
						return null;
					}

					const errorEnvelope: ErrorEventEnvelope = {
						...baseEnvelope,
						kind: 'error',
						messageId: parsed.messageId ? String(parsed.messageId) : undefined,
						sequenceId: parsed.sequenceId ? Number(parsed.sequenceId) : undefined,
						payload: {
							message: String(parsed.payload.message),
							code: parsed.payload.code ? String(parsed.payload.code) : undefined
						}
					};
					return errorEnvelope;
				}

				default: {
					// For streaming chunks and message complete events
					if (!parsed.messageId || !parsed.sequenceId || !parsed.payload) {
						console.warn('Invalid stream/message event: missing required fields', { parsed });
						return null;
					}

					// Create envelope based on whether this is a stream chunk or complete message
					// We can distinguish by the event type that called this method
					const envelope: StreamChunkEventEnvelope | MessageCompleteEventEnvelope = {
						...baseEnvelope,
						messageId: String(parsed.messageId),
						sequenceId: Number(parsed.sequenceId),
						payload: parsed.payload as any // Will be properly typed by the event processor
					};
					return envelope;
				}
			}
		} catch (error) {
			console.error('Error parsing event envelope:', error, { eventData });
			return null;
		}
	}

	/**
	 * Legacy method for backward compatibility
	 * @deprecated Use the strongly-typed parseEventEnvelope method instead
	 */
	parseEventEnvelopeLegacy(eventData: string): ServerEventEnvelope | null {
		try {
			const parsed = JSON.parse(eventData);

			// Validate required fields
			if (!parsed || typeof parsed !== 'object') {
				console.warn('Invalid event envelope: not an object', { eventData });
				return null;
			}

			// chatId and kind are required for all events
			if (!parsed.chatId || !parsed.kind) {
				console.warn('Invalid event envelope: missing required fields', { parsed });
				return null;
			}

			const envelope: ServerEventEnvelope = {
				chatId: String(parsed.chatId),
				messageId: parsed.messageId ? String(parsed.messageId) : undefined,
				sequenceId: parsed.sequenceId ? Number(parsed.sequenceId) : undefined,
				version: Number(parsed.version || 1),
				ts: String(parsed.ts || new Date().toISOString()),
				kind: String(parsed.kind),
				payload: (parsed.payload as Record<string, unknown>) || {}
			};

			return envelope;
		} catch (error) {
			console.error('Error parsing event envelope:', error, { eventData });
			return null;
		}
	}
}

/**
 * Factory function for creating SSE parser instances
 */
export function createSSEParser(): SSEParser {
	return new SSEParserImpl();
}
