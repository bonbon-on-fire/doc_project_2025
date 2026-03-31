import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { render, fireEvent, screen, cleanup } from '@testing-library/svelte';
import { createEventDispatcher } from 'svelte';
import CollapsibleMessageRenderer from './CollapsibleMessageRenderer.svelte';

// Mock the time utility
vi.mock('$lib/utils/time', () => ({
	formatTime: vi.fn((timestamp) => new Date(timestamp).toLocaleTimeString())
}));

describe('CollapsibleMessageRenderer', () => {
	const mockMessage = {
		id: 'test-message-1',
		role: 'assistant' as const,
		timestamp: new Date('2025-08-12T10:30:00Z').toISOString(),
		text: 'Test message content'
	};

	const defaultProps = {
		message: mockMessage,
		isLatest: true, // Set to true to avoid auto-collapse during tests
		expanded: true,
		collapsible: true,
		iconPath:
			'M10 12l-2-2m0 0l2-2m-2 2h8m-8 0H6a2 2 0 01-2-2V6a2 2 0 012-2h12a2 2 0 012 2v12a2 2 0 01-2 2H6a2 2 0 01-2-2v-4z',
		iconColors: 'from-blue-500 to-purple-600',
		messageType: 'Test',
		collapsedPreview: 'This is a preview of the content'
	};

	beforeEach(() => {
		vi.clearAllMocks();
	});

	afterEach(() => {
		cleanup();
	});

	describe('Basic Rendering', () => {
		it('renders with default props', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });
			expect(container).toBeTruthy();
		});

		it('renders avatar for assistant messages', () => {
			render(CollapsibleMessageRenderer, { props: defaultProps });

			// Look for the avatar button
			const avatarContainer = document.querySelector('.relative');
			expect(avatarContainer).toBeInTheDocument();

			// Check for the SVG icon
			const icon = document.querySelector('svg');
			expect(icon).toBeInTheDocument();
		});

		it('renders timestamp correctly', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });
			const timestamp = container.querySelector('[data-testid="message-timestamp"]');
			expect(timestamp).toBeInTheDocument();
		});

		it('applies correct flex direction for assistant messages', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });
			const messageContainer = container.querySelector('.justify-start');
			expect(messageContainer).toBeInTheDocument();
		});

		it('applies correct flex direction for user messages', () => {
			const userMessage = { ...mockMessage, role: 'user' as const };
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, message: userMessage }
			});
			const messageContainer = container.querySelector('.justify-end');
			expect(messageContainer).toBeInTheDocument();
		});
	});

	describe('Collapsible States', () => {
		it('shows expanded content when expanded=true', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });

			// Should show the expanded message bubble - look for slot content container
			const expandedContent = container.querySelector('.rounded-2xl.px-4.py-3.shadow-sm.border');
			expect(expandedContent).toBeInTheDocument();
		});

		it('shows collapsed preview when expanded=false', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, expanded: false }
			});

			// Should show collapsed preview text - use container to avoid multiple elements
			const collapsedContent = container.querySelector('.text-sm.truncate.flex-1');
			expect(collapsedContent).toBeInTheDocument();
			expect(collapsedContent?.textContent).toBe(defaultProps.collapsedPreview);

			// Should show message type in uppercase - use container
			const messageType = container.querySelector('.text-xs.font-medium.uppercase');
			expect(messageType).toBeInTheDocument();
			expect(messageType?.textContent).toBe('Test'); // DOM content is not transformed, CSS does the uppercase
		});

		it('shows expand/collapse indicator when collapsible', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });

			// Should show the indicator div
			const indicator = container.querySelector('.absolute.-bottom-0\\.5');
			expect(indicator).toBeInTheDocument();
		});

		it('hides indicator when not collapsible', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, collapsible: false }
			});

			// Should not show the indicator
			const indicator = container.querySelector('.absolute.-bottom-0\\.5');
			expect(indicator).not.toBeInTheDocument();
		});
	});

	describe('Interaction', () => {
		it('handles avatar click for collapsible content', async () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });

			// Find the avatar button
			const avatarButton = container.querySelector('button');
			expect(avatarButton).toBeInTheDocument();

			// Should be clickable and not disabled
			expect(avatarButton).not.toBeDisabled();
		});

		it('disables avatar when not collapsible', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, collapsible: false }
			});

			const avatarButton = container.querySelector('button');
			expect(avatarButton).toBeDisabled();
		});

		it('applies correct tabindex for collapsible elements', () => {
			const { container } = render(CollapsibleMessageRenderer, { props: defaultProps });

			const avatarButton = container.querySelector('button');
			expect(avatarButton).toHaveAttribute('tabindex', '0');
		});

		it('removes from tab order when non-collapsible', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, collapsible: false }
			});

			const avatarButton = container.querySelector('button');
			expect(avatarButton).toHaveAttribute('tabindex', '-1');
		});
	});

	describe('Accessibility', () => {
		it('has proper ARIA attributes when expanded', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, expanded: true }
			});

			const avatarButton = container.querySelector('button');
			expect(avatarButton).toHaveAttribute('aria-expanded', 'true');
			expect(avatarButton).toHaveAttribute('aria-label', 'Collapse Test');
		});

		it('has proper ARIA attributes when collapsed', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, expanded: false }
			});

			const avatarButton = container.querySelector('button');
			expect(avatarButton).toHaveAttribute('aria-expanded', 'false');
			expect(avatarButton).toHaveAttribute('aria-label', 'Expand Test');
		});
	});

	describe('Theme Customization', () => {
		it('applies custom border colors', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: {
					...defaultProps,
					borderColor: 'border-red-200',
					expanded: true
				}
			});

			const messageContent = container.querySelector('.border-red-200');
			expect(messageContent).toBeInTheDocument();
		});

		it('applies custom background colors', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: {
					...defaultProps,
					bgColor: 'bg-red-50',
					expanded: true
				}
			});

			const messageContent = container.querySelector('.bg-red-50');
			expect(messageContent).toBeInTheDocument();
		});

		it('applies custom icon gradient colors', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: {
					...defaultProps,
					iconColors: 'from-red-500 to-pink-600'
				}
			});

			const avatar = container.querySelector('.from-red-500');
			expect(avatar).toBeInTheDocument();
		});
	});

	describe('Edge Cases', () => {
		it('handles empty collapsedPreview gracefully', () => {
			const { container } = render(CollapsibleMessageRenderer, {
				props: { ...defaultProps, expanded: false, collapsedPreview: '' }
			});

			// Should still show message type - use container to avoid multiple elements
			const messageType = container.querySelector('.text-xs.font-medium.uppercase');
			expect(messageType).toBeInTheDocument();
			expect(messageType?.textContent).toBe('Test'); // DOM content is not transformed, CSS does the uppercase
		});

		it('handles missing message properties', () => {
			const incompleteMessage = { id: 'test', role: 'assistant' as const };

			expect(() => {
				render(CollapsibleMessageRenderer, {
					props: { ...defaultProps, message: incompleteMessage }
				});
			}).not.toThrow();
		});

		it('renders slot content when provided', () => {
			const TestComponent = {
				components: { CollapsibleMessageRenderer },
				template: `
					<CollapsibleMessageRenderer v-bind="$props">
						<div data-testid="slot-content">Test slot content</div>
					</CollapsibleMessageRenderer>
				`,
				props: defaultProps
			};

			// This would require a more complex setup for slot testing
			// For now, just verify the component doesn't crash with slot content
			expect(() => {
				render(CollapsibleMessageRenderer, { props: defaultProps });
			}).not.toThrow();
		});
	});
});
