/**
 * JUF Stitcher Manager
 *
 * Manages multiple concurrent JSON Fragment rebuilders for different documents.
 * Each document is identified by a unique ID and has its own isolated rebuilder.
 * Tracks completion state to prevent updates after documents are complete.
 *
 * @module jufStitcherManager
 */

import { JsonFragmentRebuilder } from './jsonFragmentRebuilder';
import type { JsonFragmentUpdate } from '../chat/sseEventTypes';

/**
 * Manager for multiple concurrent JSON document rebuilders.
 *
 * Provides isolation between different documents being rebuilt from fragments,
 * tracks completion state, and prevents updates after completion boundaries.
 *
 * @example
 * ```typescript
 * const manager = new JufStitcherManager();
 *
 * // Apply fragments for a specific document
 * const value = manager.apply('doc1', fragments);
 *
 * // Mark document as complete (e.g., when tool result received)
 * manager.complete('doc1');
 *
 * // Clean up when done
 * manager.delete('doc1');
 * ```
 */
export class JufStitcherManager {
	/** Map of document IDs to their rebuilders */
	private map = new Map<string, JsonFragmentRebuilder>();

	/** Set of document IDs that have been marked complete */
	private completed = new Set<string>();

	/**
	 * Try to get an existing rebuilder for the specified document.
	 *
	 * @param id - Unique identifier for the document
	 * @returns The rebuilder instance if it exists, undefined otherwise
	 */
	tryGet(id: string): JsonFragmentRebuilder | undefined {
		return this.map.get(id);
	}

	/**
	 * Get or create a rebuilder for the specified document.
	 * Ensures a rebuilder exists for the document.
	 *
	 * @param id - Unique identifier for the document
	 * @returns The rebuilder instance for this document
	 * @throws Error if the document has already been marked as complete
	 */
	get(id: string): JsonFragmentRebuilder {
		if (this.completed.has(id)) {
			throw new Error(`Document ${id} is already complete`);
		}

		let rebuilder = this.map.get(id);
		if (!rebuilder) {
			rebuilder = new JsonFragmentRebuilder();
			this.map.set(id, rebuilder);
		}

		return rebuilder;
	}

	/**
	 * Alias for get() with clearer create-or-get semantics.
	 *
	 * @param id - Unique identifier for the document
	 * @returns The rebuilder instance for this document
	 * @throws Error if the document has already been marked as complete
	 */
	ensure(id: string): JsonFragmentRebuilder {
		return this.get(id);
	}

	/**
	 * Apply fragment updates to a document and return its current value.
	 *
	 * If the document is already complete, updates are ignored with a warning.
	 * Automatically tracks completion if JsonComplete fragment is received.
	 *
	 * @param id - Document identifier
	 * @param updates - Array of JSON fragment updates to apply
	 * @returns The current value of the document after applying updates
	 */
	apply(id: string, updates: JsonFragmentUpdate[]): any {
		if (this.completed.has(id)) {
			// Silently ignore updates for completed documents (expected behavior)
			return this.map.get(id)?.getValue();
		}

		const rebuilder = this.get(id);
		rebuilder.apply(updates);

		// Track completion if JsonComplete was received
		if (rebuilder.isComplete()) {
			this.completed.add(id);
		}

		return rebuilder.getValue();
	}

	/**
	 * Mark a document as complete and prevent further updates.
	 *
	 * This is typically called when a tool result is received or
	 * when the message containing this document is finalized.
	 *
	 * @param id - Document identifier to mark as complete
	 */
	complete(id: string): void {
		this.completed.add(id);

		const rebuilder = this.map.get(id);
		if (rebuilder) {
			rebuilder.lockForResult();
		}
	}

	/**
	 * Remove a document's rebuilder and completion state.
	 *
	 * Frees memory associated with the document. Typically called
	 * when a message is completely finished.
	 *
	 * @param id - Document identifier to remove
	 */
	delete(id: string): void {
		this.map.delete(id);
		this.completed.delete(id);
	}

	/**
	 * Clear all documents and their state.
	 *
	 * Removes all rebuilders and completion tracking. Use with caution
	 * as this affects all managed documents.
	 */
	clear(): void {
		this.map.clear();
		this.completed.clear();
	}

	/**
	 * Get the current value of a document without creating it.
	 *
	 * @param id - Document identifier
	 * @returns The current value of the document, or undefined if not found
	 */
	getValue(id: string): any {
		return this.map.get(id)?.getValue();
	}

	/**
	 * Check if a document exists (created but not deleted).
	 *
	 * @param id - Document identifier
	 * @returns True if the document exists, false otherwise
	 */
	has(id: string): boolean {
		return this.map.has(id);
	}

	/**
	 * Check if a document has been marked as complete.
	 *
	 * @param id - Document identifier
	 * @returns True if the document is complete, false otherwise
	 */
	isComplete(id: string): boolean {
		return this.completed.has(id);
	}

	/**
	 * Get the number of documents currently managed.
	 *
	 * @returns The number of documents
	 */
	get size(): number {
		return this.map.size;
	}

	/**
	 * Get all document IDs currently managed.
	 *
	 * @returns An array of document IDs
	 */
	getDocumentIds(): string[] {
		return Array.from(this.map.keys());
	}

	/**
	 * Finalize and clean up all documents for a specific message.
	 * This is typically called when a message is completely finished.
	 *
	 * @param messageId - The message identifier
	 * @param toolCallIds - Array of tool call IDs associated with the message
	 */
	finalizeMessage(messageId: string, toolCallIds?: string[]): void {
		if (toolCallIds) {
			// Clean up specific tool call documents
			for (const toolCallId of toolCallIds) {
				const docId = `${messageId}:${toolCallId}`;
				this.delete(docId);
			}
		} else {
			// Clean up all documents that start with this messageId
			const prefix = `${messageId}:`;
			for (const docId of this.map.keys()) {
				if (docId.startsWith(prefix)) {
					this.delete(docId);
				}
			}
		}
	}
}
