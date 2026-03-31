/**
 * Represents a streaming content update with metadata about the change.
 */
export interface StreamingUpdate {
	/**
	 * The incremental content to be added to the message.
	 */
	delta: string;

	/**
	 * Whether this update marks the end of the streaming sequence.
	 */
	done: boolean;

	/**
	 * Optional metadata about the update type or content.
	 */
	metadata?: Record<string, any>;
}

/**
 * Interface for handling incremental content updates during streaming.
 * Manages buffering and optimization of real-time content updates.
 */
export interface StreamingHandler {
	/**
	 * Number of characters to buffer before triggering a render update.
	 * Higher values provide better performance but less real-time feel.
	 * Recommended range: 50-200 characters.
	 */
	readonly bufferSize: number;

	/**
	 * Creates renderable content from a streaming update.
	 * Handles buffering, formatting, and optimization of incremental updates.
	 *
	 * @param update - The streaming update containing delta content and metadata
	 * @returns Formatted content ready for rendering in the UI
	 */
	createRenderableContent(update: StreamingUpdate): string;
}
