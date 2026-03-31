/**
 * Type Validation Utilities
 *
 * This module provides runtime validation utilities to ensure that
 * SSE events received from the server match the expected TypeScript types.
 * This helps catch type mismatches between client and server at runtime.
 */

import type {
	SSEEventEnvelope,
	SSEEventEnvelopeUnion,
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope,
	ErrorEventEnvelope,
	InitPayload,
	TextStreamChunkPayload,
	ReasoningStreamChunkPayload,
	TextCompletePayload,
	ReasoningCompletePayload,
	UsageCompletePayload,
	ErrorPayload
} from './sseEventTypes';

/**
 * Validation result with detailed error information
 */
export interface ValidationResult {
	isValid: boolean;
	errors: string[];
	warnings: string[];
}

/**
 * Validator for SSE event envelopes
 * Provides comprehensive validation of server events against client types
 */
export class SSEEventValidator {
	/**
	 * Validate that a parsed object matches the expected SSE event envelope structure
	 */
	static validateSSEEventEnvelope(obj: any): ValidationResult {
		const errors: string[] = [];
		const warnings: string[] = [];

		// Check required base properties
		if (!obj || typeof obj !== 'object') {
			errors.push('Event envelope must be an object');
			return { isValid: false, errors, warnings };
		}

		if (!obj.chatId || typeof obj.chatId !== 'string') {
			errors.push('Event envelope must have a valid chatId (string)');
		}

		if (typeof obj.version !== 'number') {
			warnings.push('Event envelope should have a version number');
		}

		if (!obj.ts || typeof obj.ts !== 'string') {
			warnings.push('Event envelope should have a timestamp (ts)');
		}

		if (!obj.kind || typeof obj.kind !== 'string') {
			errors.push('Event envelope must have a valid kind (string)');
		}

		return { isValid: errors.length === 0, errors, warnings };
	}

	/**
	 * Validate an initialization event envelope
	 */
	static validateInitEventEnvelope(obj: any): ValidationResult {
		const baseResult = this.validateSSEEventEnvelope(obj);
		if (!baseResult.isValid) return baseResult;

		const errors = [...baseResult.errors];
		const warnings = [...baseResult.warnings];

		if (obj.kind !== 'meta') {
			errors.push('Init event must have kind "meta"');
		}

		if (!obj.payload || typeof obj.payload !== 'object') {
			errors.push('Init event must have a payload object');
		} else {
			if (!obj.payload.userMessageId || typeof obj.payload.userMessageId !== 'string') {
				errors.push('Init event payload must have a userMessageId (string)');
			}

			if (!obj.payload.userTimestamp || typeof obj.payload.userTimestamp !== 'string') {
				errors.push('Init event payload must have a userTimestamp (string)');
			}

			if (typeof obj.payload.userSequenceNumber !== 'number') {
				errors.push('Init event payload must have a userSequenceNumber (number)');
			}
		}

		return { isValid: errors.length === 0, errors, warnings };
	}

	/**
	 * Validate a stream chunk event envelope
	 */
	static validateStreamChunkEventEnvelope(obj: any): ValidationResult {
		const baseResult = this.validateSSEEventEnvelope(obj);
		if (!baseResult.isValid) return baseResult;

		const errors = [...baseResult.errors];
		const warnings = [...baseResult.warnings];

		if (!obj.messageId || typeof obj.messageId !== 'string') {
			errors.push('Stream chunk event must have a messageId (string)');
		}

		if (typeof obj.sequenceId !== 'number') {
			errors.push('Stream chunk event must have a sequenceId (number)');
		}

		if (!obj.payload || typeof obj.payload !== 'object') {
			errors.push('Stream chunk event must have a payload object');
		} else {
			if (!obj.payload.delta || typeof obj.payload.delta !== 'string') {
				errors.push('Stream chunk payload must have a delta (string)');
			}

			// Validate specific payload types
			if ('done' in obj.payload && typeof obj.payload.done !== 'boolean') {
				errors.push('Text stream chunk payload done property must be boolean');
			}

			if (
				'visibility' in obj.payload &&
				obj.payload.visibility &&
				typeof obj.payload.visibility !== 'string'
			) {
				errors.push('Reasoning stream chunk payload visibility property must be string');
			}
		}

		return { isValid: errors.length === 0, errors, warnings };
	}

	/**
	 * Validate a message complete event envelope
	 */
	static validateMessageCompleteEventEnvelope(obj: any): ValidationResult {
		const baseResult = this.validateSSEEventEnvelope(obj);
		if (!baseResult.isValid) return baseResult;

		const errors = [...baseResult.errors];
		const warnings = [...baseResult.warnings];

		if (!obj.messageId || typeof obj.messageId !== 'string') {
			errors.push('Message complete event must have a messageId (string)');
		}

		if (typeof obj.sequenceId !== 'number') {
			errors.push('Message complete event must have a sequenceId (number)');
		}

		if (!obj.payload || typeof obj.payload !== 'object') {
			errors.push('Message complete event must have a payload object');
		} else {
			// Validate specific payload types based on what properties exist
			if ('text' in obj.payload && typeof obj.payload.text !== 'string') {
				errors.push('Text complete payload text property must be string');
			}

			if ('reasoning' in obj.payload && typeof obj.payload.reasoning !== 'string') {
				errors.push('Reasoning complete payload reasoning property must be string');
			}

			if (
				'usage' in obj.payload &&
				(typeof obj.payload.usage !== 'object' || obj.payload.usage === null)
			) {
				errors.push('Usage complete payload usage property must be object');
			}

			if (
				'visibility' in obj.payload &&
				obj.payload.visibility &&
				typeof obj.payload.visibility !== 'string'
			) {
				errors.push('Complete payload visibility property must be string');
			}
		}

		return { isValid: errors.length === 0, errors, warnings };
	}

	/**
	 * Validate a stream complete event envelope
	 */
	static validateStreamCompleteEventEnvelope(obj: any): ValidationResult {
		const baseResult = this.validateSSEEventEnvelope(obj);
		if (!baseResult.isValid) return baseResult;

		const errors = [...baseResult.errors];

		if (obj.kind !== 'complete') {
			errors.push('Stream complete event must have kind "complete"');
		}

		return {
			isValid: errors.length === 0,
			errors: [...baseResult.errors, ...errors],
			warnings: baseResult.warnings
		};
	}

	/**
	 * Validate an error event envelope
	 */
	static validateErrorEventEnvelope(obj: any): ValidationResult {
		const baseResult = this.validateSSEEventEnvelope(obj);
		if (!baseResult.isValid) return baseResult;

		const errors = [...baseResult.errors];
		const warnings = [...baseResult.warnings];

		if (obj.kind !== 'error') {
			errors.push('Error event must have kind "error"');
		}

		if (!obj.payload || typeof obj.payload !== 'object') {
			errors.push('Error event must have a payload object');
		} else {
			if (!obj.payload.message || typeof obj.payload.message !== 'string') {
				errors.push('Error event payload must have a message (string)');
			}

			if ('code' in obj.payload && obj.payload.code && typeof obj.payload.code !== 'string') {
				errors.push('Error event payload code property must be string');
			}
		}

		if ('messageId' in obj && obj.messageId && typeof obj.messageId !== 'string') {
			errors.push('Error event messageId property must be string');
		}

		if ('sequenceId' in obj && obj.sequenceId && typeof obj.sequenceId !== 'number') {
			errors.push('Error event sequenceId property must be number');
		}

		return { isValid: errors.length === 0, errors, warnings };
	}

	/**
	 * Validate any SSE event envelope based on its kind
	 */
	static validateEventEnvelope(obj: any): ValidationResult {
		if (!obj || !obj.kind) {
			return {
				isValid: false,
				errors: ['Cannot validate event: missing or invalid kind property'],
				warnings: []
			};
		}

		switch (obj.kind) {
			case 'meta':
				return this.validateInitEventEnvelope(obj);
			case 'complete':
				return this.validateStreamCompleteEventEnvelope(obj);
			case 'error':
				return this.validateErrorEventEnvelope(obj);
			default:
				// For messageupdate and message events, we need additional context
				// to determine if it's a stream chunk or message complete
				if (obj.messageId && obj.sequenceId) {
					// This could be either - validate common structure
					const streamResult = this.validateStreamChunkEventEnvelope(obj);
					const messageResult = this.validateMessageCompleteEventEnvelope(obj);

					// Return the result with fewer errors
					return streamResult.errors.length <= messageResult.errors.length
						? streamResult
						: messageResult;
				}

				return {
					isValid: false,
					errors: [`Unknown event kind: ${obj.kind}`],
					warnings: []
				};
		}
	}
}

/**
 * Utility function to validate and log SSE event issues
 */
export function validateAndLogSSEEvent(obj: any, eventType: string): boolean {
	const result = SSEEventValidator.validateEventEnvelope(obj);

	if (!result.isValid) {
		console.error(`Invalid ${eventType} SSE event:`, {
			event: obj,
			errors: result.errors,
			warnings: result.warnings
		});
		return false;
	}

	if (result.warnings.length > 0) {
		console.warn(`${eventType} SSE event warnings:`, {
			event: obj,
			warnings: result.warnings
		});
	}

	return true;
}
