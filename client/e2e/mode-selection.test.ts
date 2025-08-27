import { test, expect } from '@playwright/test';

/**
 * E2E Tests for Chat Mode Selection Feature
 *
 * Test Scenarios:
 * 1. Mode selector visibility and loading
 * 2. Mode selection and chat creation
 * 3. Mode switching during conversation
 * 4. Visual feedback and notifications
 * 5. Custom mode creation and usage
 * 6. Error handling and tool warnings
 *
 * Uses established patterns from existing E2E tests
 */

test.describe('Mode Selection E2E Tests', () => {
	test.beforeEach(async ({ page }) => {
		// Clean up any existing chats to ensure ModeSelector is visible
		try {
			// Get all chats and delete them via API
			const response = await page.request.get('http://localhost:5099/api/chat/history/user123');
			if (response.ok()) {
				const data = await response.json();
				if (data.chats && data.chats.length > 0) {
					// Delete all existing chats
					for (const chat of data.chats) {
						await page.request.delete(`http://localhost:5099/api/chat/${chat.id}`);
					}
					console.log(`Cleaned up ${data.chats.length} existing chats`);
				}
			}
		} catch (error) {
			console.log('Note: Could not clean up chats via API:', error);
		}

		// Navigate to the app and wait for it to be ready
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');

		// Wait for the chat interface to be ready
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();

		// Give time for the chat state to update after cleanup
		await page.waitForTimeout(1000);
	});

	test('Test 1: Mode selector visibility and loading', async ({ page }) => {
		console.log('🎯 Test 1: Testing mode selector visibility and loading');

		// Debug: Take screenshot and check page state
		await page.screenshot({ path: 'debug-mode-selector-test.png', fullPage: true });

		// Debug: Check if currentChatId is set
		const currentChatId = await page.evaluate(() => {
			// Try to access the store state through any global references
			return document.querySelector('[data-testid="chat-sidebar"]')
				? 'chat-sidebar-present'
				: 'no-chat-sidebar';
		});
		console.log('Debug - Chat sidebar state:', currentChatId);

		// Debug: Check if ModeSelector element exists at all
		const modeSelectorExists = await page.locator('[data-testid="mode-selector"]').count();
		console.log('Debug - ModeSelector elements found:', modeSelectorExists);

		// Check if mode selector is visible in welcome screen
		const modeSelector = page.getByTestId('mode-selector');
		await expect(modeSelector).toBeVisible({ timeout: 10000 });
		console.log('✅ Step 1a: Mode selector visible in welcome screen');

		// Click on mode selector to open dropdown
		await modeSelector.click();

		// Verify dropdown opens and shows modes
		const modeDropdown = page.getByTestId('mode-dropdown');
		await expect(modeDropdown).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 1b: Mode dropdown opens successfully');

		// Check for mode categories
		await expect(page.getByText('Task Modes')).toBeVisible({ timeout: 5000 });
		await expect(page.getByText('System Modes')).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 1c: Mode categories are displayed');

		// Verify at least some default modes are present
		const generalMode = page.getByTestId('mode-option-general');
		const codingMode = page.getByTestId('mode-option-coding');

		await expect(generalMode.or(page.getByText('General', { exact: false }))).toBeVisible({
			timeout: 5000
		});
		console.log('✅ Step 1d: Default modes are loaded');

		// Close dropdown by clicking outside
		await page.click('body', { position: { x: 10, y: 10 } });
		await expect(modeDropdown).not.toBeVisible({ timeout: 3000 });
		console.log('✅ Step 1e: Dropdown closes when clicking outside');
	});

	test('Test 2: Mode selection and chat creation with selected mode', async ({ page }) => {
		console.log('🎭 Test 2: Testing mode selection and chat creation');

		// Select a specific mode (e.g., Coding mode)
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();

		// Wait for dropdown and select coding mode
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });
		const codingModeOption = page
			.getByTestId('mode-option-coding')
			.or(page.getByText('Coding', { exact: false }));
		await codingModeOption.click();
		console.log('✅ Step 2a: Selected Coding mode');

		// Verify mode is selected (selector should show the mode name)
		await expect(modeSelector).toContainText('Coding', { timeout: 5000 });
		console.log('✅ Step 2b: Mode selector shows selected mode');

		// Send a message with the selected mode
		const testMessage = 'Write a Python function to calculate factorial';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);

		const sendButton = page.getByRole('button', { name: 'New Chat' });
		await sendButton.click();
		console.log('✅ Step 2c: Message sent with selected mode');

		// Wait for conversation creation and navigation
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		console.log('✅ Step 2d: Conversation created successfully');

		// Verify mode is displayed in chat header
		const chatHeader = page.getByTestId('chat-header');
		await expect(chatHeader).toContainText('Coding', { timeout: 5000 });
		console.log('✅ Step 2e: Mode displayed in chat header');

		// Verify user message appears
		await expect(page.getByTestId('message-content').first()).toContainText('factorial', {
			timeout: 10000
		});
		console.log('✅ Step 2f: User message visible with mode context');

		// Wait for AI response
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});
		console.log('✅ Step 2g: AI responded with mode-specific context');
	});

	test('Test 3: Mode switching during conversation', async ({ page }) => {
		console.log('🔄 Test 3: Testing mode switching mid-conversation');

		// Start with General mode
		const modeSelector = page.getByTestId('mode-selector');
		await expect(modeSelector).toContainText('General', { timeout: 10000 });

		// Create a conversation
		const initialMessage = 'Hello, I need general help';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(initialMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for conversation to load
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});
		console.log('✅ Step 3a: Initial conversation created with General mode');

		// Switch to Writing mode
		const headerModeSelector = page
			.getByTestId('header-mode-selector')
			.or(page.getByTestId('mode-selector').nth(1));
		await headerModeSelector.click();

		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });
		const writingModeOption = page
			.getByTestId('mode-option-writing')
			.or(page.getByText('Writing', { exact: false }));
		await writingModeOption.click();
		console.log('✅ Step 3b: Switched to Writing mode');

		// Verify toast notification appears
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		await expect(toast).toContainText('Mode switched', { timeout: 5000 });
		console.log('✅ Step 3c: Toast notification shown for mode switch');

		// Send a follow-up message with new mode
		const followUpMessage = 'Now help me write a short story about a robot';
		const chatMessageInput = page.getByRole('textbox', { name: 'Type your message...' });
		await chatMessageInput.fill(followUpMessage);
		await page.getByRole('button', { name: 'Send message (Enter)' }).click();
		console.log('✅ Step 3d: Follow-up message sent with Writing mode');

		// Verify message appears and AI responds
		await expect(page.getByTestId('message-content').nth(2)).toContainText('robot', {
			timeout: 10000
		});
		await expect(page.getByTestId('message-count')).toHaveText(/\b4 messages\b/, {
			timeout: 25000
		});
		console.log('✅ Step 3e: AI responded with writing mode context');

		// Verify mode persists in header
		await expect(page.getByTestId('chat-header')).toContainText('Writing', { timeout: 5000 });
		console.log('✅ Step 3f: Mode persists in chat header after switch');
	});

	test('Test 4: Visual feedback and mode information display', async ({ page }) => {
		console.log('🎨 Test 4: Testing visual feedback and mode information');

		// Open mode selector
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		// Hover over a mode to see tooltip with description
		const codingMode = page
			.getByTestId('mode-option-coding')
			.or(page.getByText('Coding', { exact: false }))
			.first();
		await codingMode.hover();

		// Check for tooltip with mode description
		const tooltip = page.getByRole('tooltip').or(page.getByTestId('mode-tooltip'));
		await expect(tooltip).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 4a: Mode tooltip shows on hover');

		// Check for tool count in mode option
		const toolCount = page
			.getByTestId('mode-tool-count')
			.or(page.getByText(/\d+ tools?/, { exact: false }));
		await expect(toolCount.first()).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 4b: Tool count displayed for modes');

		// Select a mode and verify visual feedback
		await codingMode.click();

		// Check for selection animation/highlight
		await expect(modeSelector).toHaveClass(/.*selected.*|.*active.*/, { timeout: 5000 });
		console.log('✅ Step 4c: Visual feedback for selected mode');

		// Create a chat to see mode indicator
		const testMessage = 'Test visual indicators';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat', { timeout: 10000 });

		// Check for mode badge/indicator in chat
		const modeBadge = page
			.getByTestId('mode-badge')
			.or(page.getByTestId('chat-header').locator('[data-mode]'));
		await expect(modeBadge.first()).toBeVisible({ timeout: 10000 });
		console.log('✅ Step 4d: Mode badge visible in active chat');
	});

	test('Test 5: Tool availability warnings', async ({ page }) => {
		console.log('⚠️ Test 5: Testing tool availability warnings');

		// Open mode selector
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		// Look for any mode with tool warnings (if any)
		const warningIcon = page
			.getByTestId('tool-warning')
			.or(page.locator('[class*="warning"]').first());

		// If warnings exist, verify they're displayed properly
		const hasWarnings = await warningIcon.isVisible().catch(() => false);
		if (hasWarnings) {
			await warningIcon.hover();

			// Check for warning tooltip
			const warningTooltip = page.getByRole('tooltip').or(page.getByTestId('warning-tooltip'));
			await expect(warningTooltip).toBeVisible({ timeout: 5000 });
			await expect(warningTooltip).toContainText(/tool|unavailable|missing/i, { timeout: 5000 });
			console.log('✅ Step 5a: Tool warnings displayed when applicable');
		} else {
			console.log('ℹ️ Step 5a: No tool warnings present (all tools available)');
		}

		// Select a mode and create chat
		const generalMode = page
			.getByTestId('mode-option-general')
			.or(page.getByText('General', { exact: false }))
			.first();
		await generalMode.click();

		const testMessage = 'Test message for tool availability';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

		// If there were warnings, they should persist in chat view
		if (hasWarnings) {
			const chatWarning = page
				.getByTestId('chat-tool-warning')
				.or(page.locator('[class*="tool-warning"]').first());
			await expect(chatWarning).toBeVisible({ timeout: 5000 });
			console.log('✅ Step 5b: Tool warnings persist in chat view');
		}

		console.log('✅ Step 5c: Tool availability handled gracefully');
	});

	test('Test 6: Performance - Mode loading and switching', async ({ page }) => {
		console.log('⚡ Test 6: Testing performance of mode operations');

		// Measure initial mode loading time
		const startTime = Date.now();
		const modeSelector = page.getByTestId('mode-selector');
		await expect(modeSelector).toBeVisible({ timeout: 10000 });
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });
		const loadTime = Date.now() - startTime;

		console.log(`✅ Step 6a: Modes loaded in ${loadTime}ms`);
		expect(loadTime).toBeLessThan(2000); // Should load within 2 seconds

		// Measure mode switching time
		const codingMode = page
			.getByTestId('mode-option-coding')
			.or(page.getByText('Coding', { exact: false }))
			.first();

		const switchStartTime = Date.now();
		await codingMode.click();
		await expect(modeSelector).toContainText('Coding', { timeout: 5000 });
		const switchTime = Date.now() - switchStartTime;

		console.log(`✅ Step 6b: Mode switched in ${switchTime}ms`);
		expect(switchTime).toBeLessThan(500); // Should switch within 500ms

		// Test rapid mode switching
		await modeSelector.click();
		await page
			.getByTestId('mode-option-writing')
			.or(page.getByText('Writing', { exact: false }))
			.first()
			.click();

		await modeSelector.click();
		await page
			.getByTestId('mode-option-general')
			.or(page.getByText('General', { exact: false }))
			.first()
			.click();

		// Verify UI remains responsive
		await expect(modeSelector).toContainText('General', { timeout: 5000 });
		console.log('✅ Step 6c: Rapid mode switching handled smoothly');

		// Create chat and measure response time with mode
		const chatStartTime = Date.now();
		const testMessage = 'Performance test message';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		const chatCreateTime = Date.now() - chatStartTime;

		console.log(`✅ Step 6d: Chat created with mode in ${chatCreateTime}ms`);
		expect(chatCreateTime).toBeLessThan(5000); // Should create within 5 seconds
	});

	test('Test 7: Mode persistence across page refresh', async ({ page }) => {
		console.log('💾 Test 7: Testing mode persistence');

		// Select a specific mode
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		const writingMode = page
			.getByTestId('mode-option-writing')
			.or(page.getByText('Writing', { exact: false }))
			.first();
		await writingMode.click();
		await expect(modeSelector).toContainText('Writing', { timeout: 5000 });
		console.log('✅ Step 7a: Selected Writing mode');

		// Create a chat with the mode
		const testMessage = 'Test persistence with writing mode';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat/*', { timeout: 10000 });
		const chatUrl = page.url();
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		console.log('✅ Step 7b: Chat created with mode');

		// Reload the page
		await page.reload();
		await page.waitForLoadState('networkidle');
		console.log('✅ Step 7c: Page reloaded');

		// Verify we're still on the same chat
		expect(page.url()).toBe(chatUrl);

		// Check if mode is still displayed in chat header
		await expect(page.getByTestId('chat-header')).toContainText('Writing', { timeout: 10000 });
		console.log('✅ Step 7d: Mode persisted after refresh');

		// Verify mode selector shows correct mode
		const headerModeSelector = page
			.getByTestId('header-mode-selector')
			.or(page.getByTestId('mode-selector'));
		await expect(headerModeSelector.first()).toContainText('Writing', { timeout: 5000 });
		console.log('✅ Step 7e: Mode selector shows persisted mode');
	});
});
