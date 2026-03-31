import { describe, test, expect, vi } from 'vitest';
import type {
	MessageRenderer,
	CustomRenderer,
	StreamingHandler,
	StreamingUpdate,
	MessageDto,
	TextMessageDto
} from './index.js';

describe('Core TypeScript Interfaces', () => {
	describe('MessageRenderer Interface', () => {
		test('should accept valid MessageRenderer implementation', () => {
			// Mock MessageDto for testing
			const mockMessage: MessageDto & { text?: string } = {
				id: 'test-message-1',
				chatId: 'test-chat-1',
				role: 'assistant',
				text: 'Test content',
				timestamp: new Date(),
				sequenceNumber: 1
			};

			// Valid implementation with required properties
			const validRenderer: MessageRenderer<MessageDto> = {
				messageType: 'text'
			};

			expect(validRenderer.messageType).toBe('text');
			expect(validRenderer.onExpand).toBeUndefined(); // Optional method
		});

		test('should accept implementation with optional onExpand method', () => {
			const onExpandSpy = vi.fn();

			const rendererWithExpand: MessageRenderer<MessageDto> = {
				messageType: 'reasoning',
				onExpand: onExpandSpy
			};

			expect(rendererWithExpand.messageType).toBe('reasoning');
			expect(rendererWithExpand.onExpand).toBeDefined();

			// Test that the optional method can be called
			rendererWithExpand.onExpand?.();
			expect(onExpandSpy).toHaveBeenCalledOnce();
		});

		test('should enforce readonly messageType property', () => {
			const renderer: MessageRenderer<MessageDto> = {
				messageType: 'tool_call'
			};

			expect(renderer.messageType).toBe('tool_call');
			// TypeScript should prevent reassignment (compile-time check)
			// renderer.messageType = 'different'; // This would cause a compile error
		});

		test('should support generic type constraints', () => {
			// Extended message type
			interface ExtendedMessageDto extends MessageDto {
				customField: string;
			}

			const extendedRenderer: MessageRenderer<ExtendedMessageDto> = {
				messageType: 'extended'
			};

			expect(extendedRenderer.messageType).toBe('extended');
		});
	});

	describe('CustomRenderer Interface', () => {
		test('should accept valid CustomRenderer implementation', () => {
			const validCustomRenderer: CustomRenderer<string> = {
				rendererName: 'text-viewer'
			};

			expect(validCustomRenderer.rendererName).toBe('text-viewer');
			expect(validCustomRenderer.getPreviewContent).toBeUndefined(); // Optional method
		});

		test('should accept implementation with optional getPreviewContent method', () => {
			const mockData = { id: 1, text: 'Sample data' };

			const rendererWithPreview: CustomRenderer<typeof mockData> = {
				rendererName: 'data-viewer',
				getPreviewContent: (data) => `Preview: ${data.text}`
			};

			expect(rendererWithPreview.rendererName).toBe('data-viewer');
			expect(rendererWithPreview.getPreviewContent).toBeDefined();

			// Test that the optional method works correctly
			const preview = rendererWithPreview.getPreviewContent?.(mockData);
			expect(preview).toBe('Preview: Sample data');
		});

		test('should support generic types with any data type', () => {
			interface ImageData {
				url: string;
				alt: string;
			}

			const imageRenderer: CustomRenderer<ImageData> = {
				rendererName: 'image-viewer',
				getPreviewContent: (data) => `Image: ${data.alt}`
			};

			const testData: ImageData = { url: 'test.jpg', alt: 'Test image' };
			const preview = imageRenderer.getPreviewContent?.(testData);
			expect(preview).toBe('Image: Test image');
		});

		test('should enforce readonly rendererName property', () => {
			const renderer: CustomRenderer = {
				rendererName: 'chart-viewer'
			};

			expect(renderer.rendererName).toBe('chart-viewer');
			// TypeScript should prevent reassignment (compile-time check)
			// renderer.rendererName = 'different'; // This would cause a compile error
		});
	});

	describe('StreamingHandler Interface', () => {
		test('should accept valid StreamingHandler implementation', () => {
			const mockUpdate: StreamingUpdate = {
				delta: 'Hello ',
				done: false
			};

			const validHandler: StreamingHandler = {
				bufferSize: 100,
				createRenderableContent: (update) => update.delta
			};

			expect(validHandler.bufferSize).toBe(100);
			expect(typeof validHandler.createRenderableContent).toBe('function');

			const result = validHandler.createRenderableContent(mockUpdate);
			expect(result).toBe('Hello ');
		});

		test('should handle StreamingUpdate with metadata', () => {
			const updateWithMetadata: StreamingUpdate = {
				delta: 'world!',
				done: true,
				metadata: { type: 'completion', tokens: 150 }
			};

			const handler: StreamingHandler = {
				bufferSize: 50,
				createRenderableContent: (update) => {
					if (update.done) {
						return `${update.delta} [COMPLETE]`;
					}
					return update.delta;
				}
			};

			const result = handler.createRenderableContent(updateWithMetadata);
			expect(result).toBe('world! [COMPLETE]');
		});

		test('should enforce readonly bufferSize property', () => {
			const handler: StreamingHandler = {
				bufferSize: 200,
				createRenderableContent: (update) => update.delta
			};

			expect(handler.bufferSize).toBe(200);
			// TypeScript should prevent reassignment (compile-time check)
			// handler.bufferSize = 300; // This would cause a compile error
		});
	});

	describe('StreamingUpdate Interface', () => {
		test('should accept valid StreamingUpdate objects', () => {
			const basicUpdate: StreamingUpdate = {
				delta: 'test content',
				done: false
			};

			expect(basicUpdate.delta).toBe('test content');
			expect(basicUpdate.done).toBe(false);
			expect(basicUpdate.metadata).toBeUndefined();
		});

		test('should accept StreamingUpdate with metadata', () => {
			const updateWithMetadata: StreamingUpdate = {
				delta: 'final content',
				done: true,
				metadata: {
					totalTokens: 250,
					model: 'gpt-4',
					custom: { key: 'value' }
				}
			};

			expect(updateWithMetadata.delta).toBe('final content');
			expect(updateWithMetadata.done).toBe(true);
			expect(updateWithMetadata.metadata).toEqual({
				totalTokens: 250,
				model: 'gpt-4',
				custom: { key: 'value' }
			});
		});
	});

	describe('Integration Tests', () => {
		test('should work together in a realistic scenario', () => {
			// Mock a complete message renderer implementation
			const textRenderer: MessageRenderer<TextMessageDto> = {
				messageType: 'text',
				onExpand: vi.fn()
			};

			// Mock streaming handler for text content
			const textStreamingHandler: StreamingHandler = {
				bufferSize: 100,
				createRenderableContent: (update) => {
					return update.done ? `${update.delta}` : update.delta;
				}
			};

			// Mock custom renderer for code blocks
			const codeRenderer: CustomRenderer<{ language: string; code: string }> = {
				rendererName: 'code-block',
				getPreviewContent: (data) => `${data.language}: ${data.code.slice(0, 50)}...`
			};

			// Test that all components work together
			expect(textRenderer.messageType).toBe('text');

			expect(textStreamingHandler.bufferSize).toBe(100);

			expect(codeRenderer.rendererName).toBe('code-block');

			const codeData = { language: 'typescript', code: 'interface Test { value: string; }' };
			const preview = codeRenderer.getPreviewContent?.(codeData);
			expect(preview).toBe('typescript: interface Test { value: string; }...');
		});
	});
});
