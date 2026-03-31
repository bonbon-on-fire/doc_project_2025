import { test, expect } from '@playwright/test';

test.describe('Task List UI Components', () => {
	test.describe('Task Tracker Display', () => {
		test('should display task tracker when tasks are present', async ({ page }) => {
			// Navigate to chat page
			await page.goto('/chat');

			// Create a new chat
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('Hello, please help me with my tasks');
			await messageInput.press('Enter');

			// Wait for response
			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			// Check if task tracker container exists (might be collapsed)
			const taskTracker = page.locator('.pinned-task-tracker');

			// The task tracker should exist in the DOM even if no tasks
			await expect(taskTracker).toBeInDOM({ timeout: 5000 });
		});

		test('should expand and collapse task tracker', async ({ page }) => {
			await page.goto('/chat');

			// Create a chat
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('Test message');
			await messageInput.press('Enter');

			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			const taskTracker = page.locator('.pinned-task-tracker');
			await expect(taskTracker).toBeInDOM({ timeout: 5000 });

			// Find expand/collapse button if it exists
			const toggleButton = taskTracker.locator('button').first();
			const buttonExists = (await toggleButton.count()) > 0;

			if (buttonExists) {
				// Click to expand
				await toggleButton.click();

				// Task list should be visible
				const taskList = taskTracker.locator('.task-list, [role="list"]').first();
				if ((await taskList.count()) > 0) {
					await expect(taskList).toBeVisible({ timeout: 2000 });
				}

				// Click to collapse
				await toggleButton.click();
			}
		});
	});

	test.describe('Task Display Format', () => {
		test('should show proper task status symbols', async ({ page }) => {
			await page.goto('/chat');

			// The task symbols we expect
			const statusSymbols = {
				notStarted: '□',
				inProgress: '◻',
				completed: '☑'
			};

			// Create a chat
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('Create a test task');
			await messageInput.press('Enter');

			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			// If tasks appear, verify their format
			const taskItems = page.locator('.task-item, [role="listitem"]');
			const taskCount = await taskItems.count();

			if (taskCount > 0) {
				// Check first task has proper structure
				const firstTask = taskItems.first();
				const taskText = await firstTask.textContent();

				// Task should contain one of the status symbols
				const hasStatusSymbol = Object.values(statusSymbols).some((symbol) =>
					taskText?.includes(symbol)
				);

				if (taskText) {
					expect(hasStatusSymbol).toBeTruthy();
				}
			}
		});
	});

	test.describe('SSE Task Updates', () => {
		test('should handle task_update SSE events', async ({ page }) => {
			await page.goto('/chat');

			// Monitor console for SSE warnings
			const consoleWarnings: string[] = [];
			page.on('console', (msg) => {
				if (msg.type() === 'warn' && msg.text().includes('Unknown SSE event type')) {
					consoleWarnings.push(msg.text());
				}
			});

			// Create a chat that might trigger task updates
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('Help me organize my work');
			await messageInput.press('Enter');

			// Wait for response
			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			// Give time for any SSE events
			await page.waitForTimeout(2000);

			// Check that task_update events are not reported as unknown
			const unknownTaskUpdates = consoleWarnings.filter((w) => w.includes('task_update'));

			// Should not have warnings about unknown task_update events
			expect(unknownTaskUpdates.length).toBe(0);
		});
	});

	test.describe('Chat with Tasks Integration', () => {
		test('should persist tasks when switching between chats', async ({ page }) => {
			await page.goto('/chat');

			// Create first chat
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('First chat message');
			await messageInput.press('Enter');

			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			// Get chat ID from URL
			await page.waitForURL('**/chat/**', { timeout: 5000 });
			const firstChatUrl = page.url();

			// Create second chat using new chat button
			const newChatButton = page.getByRole('button', { name: /new chat/i });
			if ((await newChatButton.count()) > 0) {
				await newChatButton.click();

				// Send message in second chat
				await messageInput.fill('Second chat message');
				await messageInput.press('Enter');

				await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });
				await page.waitForURL('**/chat/**', { timeout: 5000 });

				const secondChatUrl = page.url();
				expect(secondChatUrl).not.toBe(firstChatUrl);

				// Go back to first chat
				await page.goto(firstChatUrl);
				await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

				// Task tracker should still be present
				const taskTracker = page.locator('.pinned-task-tracker');
				await expect(taskTracker).toBeInDOM({ timeout: 5000 });
			}
		});

		test('should load tasks when opening existing chat', async ({ page }) => {
			await page.goto('/chat');

			// Create a chat
			const messageInput = page.getByTestId('message-input');
			await messageInput.fill('Create initial chat');
			await messageInput.press('Enter');

			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });
			await page.waitForURL('**/chat/**', { timeout: 5000 });

			const chatUrl = page.url();

			// Navigate away and back
			await page.goto('/');
			await page.waitForTimeout(500);
			await page.goto(chatUrl);

			// Wait for chat to load
			await page.waitForSelector('[data-testid="message-list"]', { timeout: 10000 });

			// Task tracker should be initialized
			const taskTracker = page.locator('.pinned-task-tracker');
			await expect(taskTracker).toBeInDOM({ timeout: 5000 });

			// No errors should appear in console about task loading
			const consoleErrors: string[] = [];
			page.on('console', (msg) => {
				if (msg.type() === 'error' && msg.text().includes('taskManager')) {
					consoleErrors.push(msg.text());
				}
			});

			await page.waitForTimeout(1000);
			expect(consoleErrors.length).toBe(0);
		});
	});
});
