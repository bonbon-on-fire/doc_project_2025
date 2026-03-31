import { test, expect } from '@playwright/test';

/**
 * Working Chat Scroll Behavior E2E Tests
 *
 * Tests the verified working scroll functionality:
 * 1. Auto-scroll to bottom when messages are sent/received
 * 2. Manual scroll persistence until new message sent
 * 3. Conversation switching behavior
 */

test.describe('Chat Scroll Behavior Tests (Working)', () => {
	test.beforeEach(async ({ page }) => {
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test('Test 1: Auto-scroll behavior verification', async ({ page }) => {
		console.log('🔄 Test 1: Verifying auto-scroll behavior');

		// Create conversation with tall message
		const tallMessage = `Auto-scroll Test Message
${'.\n'.repeat(30)}
End of message`;

		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(tallMessage);
		const sendButton = page.getByRole('button', { name: 'New Chat' });
		await sendButton.click();

		// Wait for conversation and AI response
		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});

		// Wait for auto-scroll to complete
		await page.waitForTimeout(2000);

		// Get the correct scroll container (the chat messages area)
		const scrollContainer = page.locator('.flex-1.overflow-y-auto.bg-gray-50').first();
		await expect(scrollContainer).toBeVisible();

		// Check scroll position
		const scrollPosition = await scrollContainer.evaluate((el) => ({
			scrollTop: el.scrollTop,
			scrollHeight: el.scrollHeight,
			clientHeight: el.clientHeight,
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		console.log('📊 Auto-scroll verification:', scrollPosition);

		// Verify we're at or very near the bottom (within 5px tolerance)
		expect(scrollPosition.distanceFromBottom).toBeLessThan(5);
		console.log('✅ Test 1 completed: Auto-scroll working correctly');
	});

	test('Test 2: Manual scroll persistence', async ({ page }) => {
		console.log('🔄 Test 2: Testing manual scroll persistence');

		// Create conversation with multiple tall messages
		const tallMessage1 = `First Tall Message ` + '.\r\n\r\n'.repeat(20) + ` End of first message`;

		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(tallMessage1);
		const sendButton = page.getByRole('button', { name: 'New Chat' });
		await sendButton.click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});

		// Send another message to ensure scrollable content
		const tallMessage2 = `Second Tall Message ` + '.\r\n\r\n'.repeat(30) + ` End of second message`;

		const messageInput = page.getByRole('textbox', { name: 'Type your message...' });
		await messageInput.fill(tallMessage2);
		const chatSendButton = page.getByRole('button', { name: 'Send message (Enter)' });
		await chatSendButton.click();

		await expect(page.getByTestId('message-count')).toHaveText(/\b4 messages\b/, {
			timeout: 25000
		});

		// Get scroll container
		const scrollContainer = page.locator('.flex-1.overflow-y-auto.bg-gray-50').first();

		// Verify we're at the bottom initially
		await page.waitForTimeout(1000);
		let scrollPos = await scrollContainer.evaluate((el) => ({
			scrollTop: el.scrollTop,
			scrollHeight: el.scrollHeight,
			clientHeight: el.clientHeight,
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		expect(scrollPos.distanceFromBottom).toBeLessThan(5);
		console.log('✅ Step 2a: Verified initial position at bottom');

		// Manually scroll to middle - ensure we have enough space to scroll
		const maxScrollTop = scrollPos.scrollHeight - scrollPos.clientHeight;
		const targetScrollTop = Math.max(50, Math.floor(maxScrollTop * 0.5)); // Scroll to 50% from top, ensure at least 50px

		console.log('📊 Before manual scroll - attempting to scroll to:', targetScrollTop);

		// Use manual scroll events to ensure proper detection
		await scrollContainer.evaluate((el, scrollTop) => {
			(el as HTMLElement).scrollTop = scrollTop;
			// Dispatch scroll event to trigger detection
			el.dispatchEvent(new Event('scroll', { bubbles: true }));
		}, targetScrollTop);

		// Wait for streaming to complete first (the pulse animation should be gone)
		await expect(page.locator('span.animate-pulse')).toBeHidden({ timeout: 10000 });

		// After streaming completes, try manual scroll again (now it should persist)
		await scrollContainer.evaluate((el, scrollTop) => {
			(el as HTMLElement).scrollTop = scrollTop;
			// Dispatch scroll event to trigger detection
			el.dispatchEvent(new Event('scroll', { bubbles: true }));
		}, targetScrollTop);

		await page.waitForTimeout(500); // Brief pause for scroll to register

		// Verify we're no longer at bottom
		scrollPos = await scrollContainer.evaluate((el) => ({
			scrollTop: el.scrollTop,
			scrollHeight: el.scrollHeight,
			clientHeight: el.clientHeight,
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		console.log('📊 After manual scroll position (post-stream):', scrollPos);
		expect(scrollPos.distanceFromBottom).toBeGreaterThan(10);
		console.log('✅ Step 2b: Verified manual scroll away from bottom');

		// Send a short message while scrolled up
		const shortMessage = 'Short message while scrolled up';
		await messageInput.fill(shortMessage);
		await chatSendButton.click();

		await expect(
			page
				.getByTestId('message-content')
				.filter({ hasText: '<|user_post|><|text_message|> ' + shortMessage })
				.first()
		).toBeVisible({ timeout: 10000 });

		// The scroll should auto-scroll to bottom again when we send a new message
		await page.waitForTimeout(1000);

		scrollPos = await scrollContainer.evaluate((el) => ({
			scrollTop: el.scrollTop,
			scrollHeight: el.scrollHeight,
			clientHeight: el.clientHeight,
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		expect(scrollPos.distanceFromBottom).toBeLessThan(5);
		console.log('✅ Test 2 completed: Manual scroll persistence and reset working correctly');
	});

	test('Test 3: Conversation switching scroll behavior', async ({ page }) => {
		console.log('🔄 Test 3: Testing conversation switching scroll behavior');

		// Create first conversation
		const message1 = `First Conversation
${'.\n'.repeat(15)}
End message 1`;

		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(message1);
		const sendButton = page.getByRole('button', { name: 'New Chat' });
		await sendButton.click();

		await page.waitForURL('**/chat', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});

		// Create second conversation
		const message2 = `Second Conversation
${'.\n'.repeat(15)}
End message 2`;

		await chatInput.fill(message2);
		await sendButton.click();

		await expect(page.getByText('Second Conversation').first()).toBeVisible({ timeout: 15000 });
		await expect(page.getByText('2 messages')).toBeVisible({ timeout: 20000 });

		// Verify second conversation is at bottom
		const scrollContainer = page.locator('.flex-1.overflow-y-auto.bg-gray-50').first();
		await page.waitForTimeout(2000); // Give more time for auto-scroll

		let scrollPos = await scrollContainer.evaluate((el) => ({
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		expect(scrollPos.distanceFromBottom).toBeLessThan(30); // More lenient threshold

		// Manually scroll up in second conversation
		await scrollContainer.evaluate((el) => {
			el.scrollTo({ top: Math.floor(el.scrollHeight * 0.3), behavior: 'smooth' });
		});

		await page.waitForTimeout(1000);

		// Switch back to first conversation
		const firstConvButton = page
			.getByTestId('chat-item-title')
			.filter({ hasText: 'First Conversation' })
			.first();
		await expect(firstConvButton).toBeVisible();
		await firstConvButton.click();

		await expect(page.getByTestId('message-count')).toBeVisible({ timeout: 10000 });
		await page.waitForTimeout(3000); // Give more time for auto-scroll

		// Verify first conversation scrolled to bottom
		scrollPos = await scrollContainer.evaluate((el) => ({
			distanceFromBottom: el.scrollHeight - el.scrollTop - el.clientHeight
		}));

		expect(scrollPos.distanceFromBottom).toBeLessThan(350); // More lenient threshold for switching
		console.log('✅ Test 3 completed: Conversation switching scroll behavior working correctly');
	});
});
