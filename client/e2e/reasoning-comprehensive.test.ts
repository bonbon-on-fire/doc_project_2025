import { test, expect } from '@playwright/test';

test.describe('Comprehensive Reasoning Tests', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test.describe('Reasoning Streaming with Mock LLM', () => {
		test('should stream reasoning-first content correctly', async ({ page }) => {
			console.log('🧠 Testing reasoning-first streaming behavior');

			// Use the mock's reasoning-first trigger
			const reasoningMessage =
				'Test reasoning capabilities\nReason: Think through this step by step';

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(reasoningMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			// Wait for navigation and message list
			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			console.log('✅ Navigation complete, checking reasoning visibility');

			// Reasoning should be visible and expanded during streaming
			const reasoningContainer = page.getByTestId('reasoning-content');
			await expect(reasoningContainer).toBeVisible({ timeout: 15000 });

			// Check that reasoning is expanded during streaming (should have expanded state)
			const reasoningRenderer = page.locator('[data-component="reasoning-renderer"]').first();
			await expect(reasoningRenderer).toBeVisible({ timeout: 10000 });

			// Verify reasoning content appears progressively
			await expect(reasoningContainer).toContainText('Test reasoning capabilities', {
				timeout: 15000
			});

			console.log('✅ Reasoning content streaming verified');

			// Wait for streaming to complete
			await expect(page.locator('span.animate-pulse')).toBeHidden({ timeout: 20000 });

			// After streaming completes, reasoning should auto-collapse
			await page.waitForTimeout(1000); // Brief pause for collapse animation

			console.log('✅ Reasoning streaming test completed');
		});

		test('should handle rapid reasoning chunks with controlled timing', async ({ page }) => {
			console.log('🕰️ Testing reasoning streaming timing control');

			// Test with instruction plan for controlled streaming
			const instructionMessage = `
<|instruction_start|>
{
  "id_message": "test-reasoning-timing",
  "reasoning": { "length": 50 },
  "messages": [
    { "text_message": { "length": 30 } }
  ]
}
<|instruction_end|>
Test controlled reasoning timing
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(instructionMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Reasoning should appear and stream with controlled timing
			const reasoningContainer = page.getByTestId('reasoning-content');
			await expect(reasoningContainer).toBeVisible({ timeout: 15000 });

			// Verify progressive content loading
			const initialContent = await reasoningContainer.textContent();

			// Wait a bit and check that content has grown
			await page.waitForTimeout(2000);
			const laterContent = await reasoningContainer.textContent();

			expect(laterContent?.length).toBeGreaterThan(initialContent?.length || 0);

			console.log('✅ Timing-controlled reasoning streaming verified');
		});
	});

	test.describe('Reasoning Collapse/Expand Functionality', () => {
		test('should show reasoning components and structure correctly', async ({ page }) => {
			console.log('📺 Testing reasoning component structure');

			const reasoningMessage =
				'Component structure test\nReason: This validates UI components are present';

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(reasoningMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Wait for reasoning to appear during streaming
			const reasoningContainer = page.getByTestId('reasoning-content');
			await expect(reasoningContainer).toBeVisible({ timeout: 15000 });

			// The reasoning renderer should be present with correct structure
			const reasoningRenderer = page.getByTestId('reasoning-renderer').first();
			await expect(reasoningRenderer).toBeVisible();

			// The toggle button should be present and functional
			const reasoningToggle = page.getByTestId('reasoning-toggle-button').first();
			await expect(reasoningToggle).toBeVisible();
			await expect(reasoningToggle).toHaveAttribute('aria-expanded');
			await expect(reasoningToggle).toHaveAttribute('aria-label');

			// Expand/collapse indicator should be visible
			const indicator = page.locator('.transform.transition-transform svg').first();
			await expect(indicator).toBeVisible();

			console.log('✅ Reasoning component structure verified');
		});

		test('should display reasoning content with proper formatting', async ({ page }) => {
			console.log('📝 Testing reasoning content display and formatting');

			const reasoningMessage =
				'Formatting test\nReason: This reasoning contains meaningful content that should be displayed properly';

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(reasoningMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Wait for reasoning content to appear
			const reasoningContainer = page.getByTestId('reasoning-content');
			await expect(reasoningContainer).toBeVisible({ timeout: 15000 });

			// Verify reasoning content contains expected text
			await expect(reasoningContainer).toContainText('meaningful content');

			// Check that reasoning has proper styling classes
			await expect(reasoningContainer).toHaveClass(/text-amber-700/);

			console.log('✅ Reasoning content formatting verified');
		});

		test('should show accessibility attributes correctly', async ({ page }) => {
			console.log('♿ Testing reasoning accessibility features');

			const reasoningMessage =
				'Accessibility test\nReason: This validates ARIA attributes and accessibility';

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(reasoningMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Wait for reasoning to appear
			const reasoningContainer = page.getByTestId('reasoning-content');
			await expect(reasoningContainer).toBeVisible({ timeout: 15000 });

			// Check toggle button accessibility
			const reasoningToggle = page.getByTestId('reasoning-toggle-button').first();
			await expect(reasoningToggle).toHaveAttribute('aria-expanded');
			await expect(reasoningToggle).toHaveAttribute('aria-label');
			await expect(reasoningToggle).toHaveAttribute('tabindex', '0');

			// Button should be focusable
			await reasoningToggle.focus();
			const isFocused = await reasoningToggle.evaluate((el) => document.activeElement === el);
			expect(isFocused).toBe(true);

			console.log('✅ Reasoning accessibility verified');
		});
	});

	test.describe('Multiple Reasoning Messages', () => {
		test('should handle multiple reasoning messages in conversation', async ({ page }) => {
			console.log('📚 Testing multiple reasoning messages with auto-collapse behavior');

			// First reasoning message
			const firstMessage = 'First reasoning\nReason: First step of thinking';

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(firstMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Wait for first reasoning to appear and complete streaming
			const firstReasoningContent = page.getByTestId('reasoning-content').first();
			await expect(firstReasoningContent).toBeVisible({ timeout: 15000 });
			await expect(firstReasoningContent).toContainText('First step', { timeout: 10000 });

			// Wait for first reasoning to complete streaming
			await expect(page.locator('span.animate-pulse').first()).toBeHidden({ timeout: 20000 });

			// Send second reasoning message
			const secondMessage = 'Second reasoning\nReason: Second step of thinking';
			const messageInput = page.getByRole('textbox', { name: 'Type your message...' });
			await messageInput.fill(secondMessage);
			await page.getByRole('button', { name: 'Send message (Enter)' }).click();

			// Wait for second reasoning to appear
			const secondReasoningContent = page.getByTestId('reasoning-content').last();
			await expect(secondReasoningContent).toBeVisible({ timeout: 15000 });
			await expect(secondReasoningContent).toContainText('Second step', { timeout: 10000 });

			// After second message starts, first reasoning should be collapsed
			// Check specifically for collapsed reasoning toggle buttons
			const collapsedReasoningButtons = page.locator(
				'[data-testid="reasoning-toggle-button"][aria-expanded="false"]'
			);
			await expect(collapsedReasoningButtons).toHaveCount(1, { timeout: 10000 });

			// Verify collapsed reasoning shows preview text in the collapsed container
			const firstCollapsedContainer = page
				.locator('[role="button"][aria-expanded="false"][aria-label="Expand Reasoning"]')
				.first();
			await expect(firstCollapsedContainer).toBeVisible();
			await expect(firstCollapsedContainer).toContainText('Reasoning'); // Message type label
			await expect(firstCollapsedContainer).toContainText('First'); // Part of preview text

			// Should have multiple reasoning renderers (both collapsed and expanded)
			const reasoningRenderers = page.getByTestId('reasoning-renderer');
			await expect(reasoningRenderers).toHaveCount(4, { timeout: 15000 }); // 2 visible 2 hidden for encrypted reasoning

			// Should have multiple toggle buttons
			const reasoningToggles = page.getByTestId('reasoning-toggle-button');
			await expect(reasoningToggles).toHaveCount(2, { timeout: 5000 });

			// Test toggle functionality - expand the first collapsed reasoning
			console.log('🔄 Testing toggle functionality for collapsed reasoning');
			const firstToggleButton = page.getByTestId('reasoning-toggle-button').first();
			await firstToggleButton.click();

			// After clicking, first reasoning should be expanded again
			await expect(firstReasoningContent).toBeVisible({ timeout: 5000 });
			await expect(firstReasoningContent).toContainText('First step');

			// Both reasoning contents should now be visible and contain expected text
			await expect(secondReasoningContent).toContainText('Second step');

			console.log('✅ Multiple reasoning messages with auto-collapse handling verified');
		});
	});
});
