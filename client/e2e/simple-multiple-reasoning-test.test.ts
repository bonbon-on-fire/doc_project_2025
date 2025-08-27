// Simple test to check multiple reasoning messages
import { test, expect } from '@playwright/test';

test('Simple multiple reasoning messages test', async ({ page }) => {
	console.log('🔍 Starting simple multiple reasoning test');

	await page.goto('http://localhost:5173/chat');
	await page.waitForLoadState('networkidle');

	// First reasoning message
	const firstMessage = 'First reasoning\nReason: First step of thinking';
	console.log('📝 Sending first message:', firstMessage);

	const input = page.getByPlaceholder('Start a new conversation...');
	await input.fill(firstMessage);
	await page.getByRole('button', { name: 'New Chat' }).click();

	await page.waitForURL('**/chat', { timeout: 10000 });
	await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

	// Wait for first reasoning to appear and complete
	const firstReasoningContent = page.getByTestId('reasoning-content').first();
	await expect(firstReasoningContent).toBeVisible({ timeout: 15000 });
	console.log('✅ First reasoning content appeared');

	// Wait for streaming to complete (pulse animation should disappear)
	await expect(page.locator('span.animate-pulse')).toBeHidden({ timeout: 20000 });
	console.log('✅ First message streaming completed');

	// Check if message input is available
	const messageInput = page.getByRole('textbox', { name: 'Type your message...' });
	await expect(messageInput).toBeVisible({ timeout: 5000 });
	console.log('✅ Message input is visible');

	// Send second reasoning message
	const secondMessage = 'Second reasoning\nReason: Second step of thinking';
	console.log('📝 Sending second message:', secondMessage);

	await messageInput.fill(secondMessage);
	await page.getByRole('button', { name: 'Send message (Enter)' }).click();

	// Wait for second reasoning to appear
	await page.waitForTimeout(2000);
	const secondReasoningContent = page.getByTestId('reasoning-content').last();
	await expect(secondReasoningContent).toBeVisible({ timeout: 15000 });
	console.log('✅ Second reasoning content appeared');

	// Check reasoning renderers
	const reasoningRenderers = page.getByTestId('reasoning-renderer');
	const rendererCount = await reasoningRenderers.count();
	console.log('🧠 Reasoning renderers found:', rendererCount);

	// Check toggle buttons
	const reasoningToggles = page.getByTestId('reasoning-toggle-button');
	const toggleCount = await reasoningToggles.count();
	console.log('🔄 Reasoning toggle buttons found:', toggleCount);

	console.log('✅ Simple multiple reasoning test completed');
});
