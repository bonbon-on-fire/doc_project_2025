import { test, expect } from '@playwright/test';

test.describe('Reasoning visibility (reasoning pills)', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test('shows reasoning block during streaming when reasoning-first is triggered', async ({
		page
	}) => {
		// Our Test SSE handler emits reasoning-first when user message contains a line with "Reason:".
		const message = 'Trigger reasoning-first\nReason: please think out loud';

		// Start new chat to trigger streaming
		const input = page.getByPlaceholder('Start a new conversation...');
		await input.fill(message);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for chat route and list
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// While streaming, the reasoning content should become visible
		// Give some time for first reasoning tokens to arrive
		const reasoning = page.getByTestId('reasoning-content');
		await expect(reasoning).toBeVisible({ timeout: 15000 });

		// Ensure it contains at least part of the preface token stream
		await expect(reasoning).toContainText('The', { timeout: 15000 });

		// Also confirm regular assistant content eventually appears (streaming completes)
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 25000
		});
	});
});
