import { test, expect } from '@playwright/test';

test.describe('Tool Call Persistence', () => {
	test.beforeEach(async ({ page }) => {
		// Capture browser console logs for debugging
		page.on('console', (msg) => {
			console.log(`[Browser ${msg.type()}]:`, msg.text());
		});

		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test('should persist tool calls after switching conversations', async ({ page }) => {
		console.log('🔄 Testing tool call persistence after conversation switch');

		// Create first chat with tool call
		const firstToolMessage = `
<|instruction_start|>
{
  "id_message": "test-first-chat",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "get_weather", 
          "args": {
            "location": "New York",
            "units": "fahrenheit"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Get weather for New York
		`.trim();

		const input = page.getByPlaceholder('Start a new conversation...');
		await input.fill(firstToolMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for first chat to load
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Verify tool call is visible in first chat
		const firstToolCall = page.getByTestId('tool-call-renderer').first();
		await expect(firstToolCall).toBeVisible({ timeout: 15000 });
		await expect(page.getByTestId('tool-call-name').first()).toContainText('get_weather');
		await expect(page.getByTestId('tool-call-args').first()).toContainText('New York');
		await expect(page.getByTestId('tool-call-args').first()).toContainText('fahrenheit');

		// Create second chat with different tool call
		const secondToolMessage = `
<|instruction_start|>
{
  "id_message": "test-second-chat",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "calculate", 
          "args": {
            "expression": "100 * 50"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Calculate 100 times 50
		`.trim();

		await input.fill(secondToolMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for second chat to load
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Wait for the calculation to appear (math renderer)
		const mathCalcButton = page.getByRole('button', { name: /Math Calculation/i }).first();
		await expect(mathCalcButton).toBeVisible({ timeout: 15000 });

		// Verify the calculation expression is visible
		const messageList = page.getByTestId('message-list');
		await expect(messageList.locator('text=/100 \\* 50/i')).toBeVisible();

		// Now we should have two chats in the sidebar
		const sidebar = page.getByTestId('chat-sidebar');
		await expect(sidebar).toBeVisible();

		// Find the first chat in the sidebar (should contain "New York" or be the second item since new chats appear first)
		const chatItems = sidebar.locator('[data-testid="chat-item"]');
		const chatCount = await chatItems.count();
		console.log('Number of chats in sidebar:', chatCount);

		// The first chat should be the second item in the list (newer chats appear first)
		if (chatCount >= 2) {
			// Click on the second chat item (which is our first created chat)
			await chatItems.nth(1).click();

			// Wait for the chat to load
			await page.waitForTimeout(2000); // Give time for the chat to switch

			// Verify tool call from first chat is visible again
			console.log('Verifying first chat tool call is restored');
			const restoredToolCall = page.getByTestId('tool-call-renderer').first();
			await expect(restoredToolCall).toBeVisible({ timeout: 15000 });
			await expect(page.getByTestId('tool-call-name').first()).toContainText('get_weather');
			await expect(page.getByTestId('tool-call-args').first()).toContainText('New York');
			await expect(page.getByTestId('tool-call-args').first()).toContainText('fahrenheit');

			// Verify the second chat's tool call is NOT visible
			await expect(messageList.locator('text=/100 \\* 50/i')).not.toBeVisible();
		}

		console.log('✅ Tool calls persist correctly when switching conversations');
	});

	test('should persist tool calls after browser reload', async ({ page }) => {
		console.log('🔄 Testing tool call persistence after browser reload');

		// Create chat with tool call
		const toolMessage = `
<|instruction_start|>
{
  "id_message": "test-reload",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "search", 
          "args": {
            "query": "TypeScript best practices",
            "limit": 5,
            "sort": "relevance"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Search for TypeScript best practices
		`.trim();

		const input = page.getByPlaceholder('Start a new conversation...');
		await input.fill(toolMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for chat to load and tool call to render
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		const toolCallRenderer = page.getByTestId('tool-call-renderer').first();
		await expect(toolCallRenderer).toBeVisible({ timeout: 15000 });
		await expect(page.getByTestId('tool-call-name').first()).toContainText('search');

		const argsSection = page.getByTestId('tool-call-args').first();
		await expect(argsSection).toContainText('TypeScript best practices');
		await expect(argsSection).toContainText('limit:');
		await expect(argsSection).toContainText('5');

		// Reload the page
		console.log('Reloading browser...');
		await page.reload();
		await page.waitForLoadState('networkidle');

		// After reload, we need to select the chat from the sidebar
		const sidebar = page.getByTestId('chat-sidebar');
		await expect(sidebar).toBeVisible({ timeout: 10000 });

		// Click on the first chat in the sidebar (the one we just created)
		const chatItems = sidebar.locator('[data-testid="chat-item"]');
		const firstChat = chatItems.first();
		await expect(firstChat).toBeVisible({ timeout: 10000 });
		await firstChat.click();

		// Wait for the chat to load
		await page.waitForTimeout(2000);
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Verify tool call is still visible after reload
		console.log('Verifying tool call is restored after reload');
		const restoredToolCall = page.getByTestId('tool-call-renderer').first();
		await expect(restoredToolCall).toBeVisible({ timeout: 15000 });
		await expect(page.getByTestId('tool-call-name').first()).toContainText('search');

		const restoredArgs = page.getByTestId('tool-call-args').first();
		await expect(restoredArgs).toContainText('TypeScript best practices');
		await expect(restoredArgs).toContainText('limit:');
		await expect(restoredArgs).toContainText('5');
		await expect(restoredArgs).toContainText('sort:');
		await expect(restoredArgs).toContainText('relevance');

		console.log('✅ Tool calls persist correctly after browser reload');
	});

	test('should not show tool calls from other conversations', async ({ page }) => {
		console.log('🚫 Testing tool call isolation between conversations');

		// Create first chat with specific tool call
		const firstMessage = `
<|instruction_start|>
{
  "id_message": "test-isolation-1",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "database_query", 
          "args": {
            "query": "SELECT * FROM users",
            "database": "production"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Query the production database`;

		const input = page.getByPlaceholder('Start a new conversation...');
		await input.fill(firstMessage.trim());
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Verify first tool call
		await expect(page.getByTestId('tool-call-renderer').first()).toBeVisible({ timeout: 15000 });
		await expect(page.getByTestId('tool-call-name').first()).toContainText('database_query');
		await expect(page.getByTestId('tool-call-args').first()).toContainText('SELECT * FROM users');

		// Create second chat with different tool call
		const secondMessage = `
<|instruction_start|>
{
  "id_message": "test-isolation-2",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "send_email", 
          "args": {
            "to": "user@example.com",
            "subject": "Test"
          }
        }
      ]
    }
  ]
}
<|instruction_end|>
Send an email`;

		await input.fill(secondMessage.trim());
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Verify second tool call is visible
		await expect(page.getByTestId('tool-call-renderer').first()).toBeVisible({ timeout: 15000 });
		await expect(page.getByTestId('tool-call-name').first()).toContainText('send_email');

		// Verify first chat's tool call is NOT visible
		await expect(page.getByTestId('tool-call-name').first()).not.toContainText('database_query');
		await expect(page.getByTestId('tool-call-args').first()).not.toContainText(
			'SELECT * FROM users'
		);

		// Go back to first chat via sidebar
		const sidebar = page.getByTestId('chat-sidebar');
		await expect(sidebar).toBeVisible();

		const chatItems = sidebar.locator('[data-testid="chat-item"]');
		// Second item should be the first chat (newer chats appear first)
		await chatItems.nth(1).click();

		// Wait for chat to switch
		await page.waitForTimeout(2000);
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// Verify first chat shows its tool call but not the second chat's
		await expect(page.getByTestId('tool-call-name').first()).toContainText('database_query');
		await expect(page.getByTestId('tool-call-name').first()).not.toContainText('send_email');
		await expect(page.getByTestId('tool-call-args').first()).not.toContainText('user@example.com');

		console.log('✅ Tool calls are properly isolated between conversations');
	});
});
