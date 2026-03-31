import { test, expect } from '@playwright/test';

/**
 * Comprehensive E2E Tests for Chat Application
 *
 * Test Scenarios:
 * 1. Create new conversation - Post message, validate conversation creation, post reply
 * 2. Refresh and load existing conversation - Load conversation, reply, validate response
 *
 * Uses Sequential Thinking Methodology for debugging and validation
 * Fixed selectors based on actual DOM structure
 */

test.describe('Chat Application E2E Tests', () => {
	test.beforeEach(async ({ page }) => {
		// Navigate to the app and wait for it to be ready
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');

		// Wait for the chat interface to be ready - use correct selector
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test('Test 1: Create new conversation with full flow', async ({ page }) => {
		console.log('📝 Test 1: Starting new conversation creation test');

		// Post new message
		const testMessage1 = 'Hello, this is my first message in a new conversation';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage1);
		const sendButton = page.getByRole('button', { name: 'New Chat' });
		await sendButton.click();

		console.log('✅ Step 1a completed: Message sent, waiting for conversation creation');

		// Wait for page to navigate to the new conversation (URL should change)
		await page.waitForURL('**/chat', { timeout: 10000 });
		// Wait for message list and first message content to appear
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });

		// Wait for conversation to appear in sidebar via test id
		const chatItem = page
			.getByTestId('chat-item-title')
			.filter({ hasText: 'Hello, this is my first message in a new conversat' })
			.first();
		await expect(chatItem).toBeVisible({ timeout: 15000 });

		console.log('✅ Step 1b completed: Conversation appears in sidebar');

		// Verify the user message appears in the main chat area
		await expect(page.getByTestId('message-content').first()).toContainText('Hello', {
			timeout: 10000
		});

		console.log('✅ Step 1c completed: User message visible in chat');

		// Wait for AI response to complete - look for message count first as it's more reliable
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});

		console.log('✅ Step 1d completed: Message count shows 2 messages (user + AI)');

		// Validate AI response content is visible - scope to main conversation area only
		// Use a more specific locator that targets the conversation messages area, not sidebar
		const conversationArea = page.locator('div').filter({ hasText: testMessage1 }).first();
		await expect(conversationArea).toBeVisible();

		// Look for AI response within the conversation area, not globally
		await expect(
			page.locator('div[class*="prose"]').getByText('<|user_pre|><|text_message|> Hello')
		).toBeVisible({ timeout: 15000 });

		// Verify no "Creating..." stuck state
		const creatingIndicator = page.getByText('Creating...');
		await expect(creatingIndicator).not.toBeVisible({ timeout: 15000 });

		console.log('✅ Step 1e completed: AI response content verified');

		// Post reply and wait for AI response
		const testMessage2 = 'This is my follow-up message in the same conversation' + Date.now();
		const chatMessageInput = page.getByRole('textbox', { name: 'Type your message...' });
		await chatMessageInput.fill(testMessage2);
		const chatSendButton = page.getByRole('button', { name: 'Send message (Enter)' });
		await chatSendButton.click();

		console.log('✅ Step 1f completed: Follow-up message sent');

		// Verify the follow-up message appears
		await expect(
			page.locator('.bg-blue-600 [data-testid="message-content"]').getByText(testMessage2).first()
		).toBeVisible({ timeout: 10000 });

		console.log('✅ Step 1g completed: Follow-up message visible');

		// Wait for AI response to follow-up - check message count increase
		await expect(page.getByText('4 messages')).toBeVisible({ timeout: 25000 });

		console.log(
			'✅ Test 1 completed successfully: Full conversation flow working with 4 messages total'
		);
	});

	test('Test 2: Refresh and click on existing conversation', async ({ page }) => {
		console.log('🚀 Starting Test 2: Load existing conversation');

		// First, create a conversation to test with
		console.log('📝 Setup: Creating initial conversation');

		const setupMessage1 = 'Setup message 1 for existing conversation test ' + Date.now();
		const setupMessage2 =
			'Setup message 2 for existing conversation test ' + Date.now() + ' second';

		const chatInput = page.getByPlaceholder('Start a new conversation...');
		const sendButton = page.getByRole('button', { name: 'New Chat' });

		// Create first message
		await chatInput.fill(setupMessage1);
		await sendButton.click();

		// Wait for conversation to be created and sidebar item to appear
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
		// Wait for sidebar item with partial text match (more robust)
		const chatItem = page
			.getByTestId('chat-item-title')
			.filter({ hasText: setupMessage1.slice(0, 25) })
			.first();
		await expect(chatItem).toBeVisible({ timeout: 15000 });

		// Wait for AI response
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 15000
		});

		// delay to ensure AI response is processed
		await page.waitForTimeout(1000); // Adjust as needed for your environment

		// Create second message using in-chat input
		const setupChatMessageInput = page.getByRole('textbox', { name: 'Type your message...' });
		const setupChatSendButton = page.getByRole('button', { name: 'Send message (Enter)' });

		await setupChatMessageInput.fill(setupMessage2);
		await setupChatSendButton.click();

		// Wait for second user message and AI response
		await page.waitForTimeout(5000); // Give time for message processing
		await expect(page.getByTestId('message-count')).toHaveText(/\b4 messages\b/, {
			timeout: 20000
		});

		console.log('✅ Setup completed: Conversation with 4 messages created');

		// Step 2a: Refresh and click on existing conversation
		console.log('📝 Step 2a: Refreshing page and loading existing conversation');

		// Refresh the page
		await page.reload();
		await page.waitForLoadState('networkidle');

		// Re-create chatItem locator after reload to avoid stale element reference
		// Use partial text match for robustness
		const chatItemAfterReload = page
			.getByTestId('chat-item-title')
			.filter({ hasText: setupMessage1.slice(0, 25) })
			.first();
		await expect(chatItemAfterReload).toBeVisible({ timeout: 15000 });

		// Click on the existing conversation
		await chatItemAfterReload.click();

		// Wait for chat interface to load - this conversation should have 4 messages
		await expect(page.getByText('4 messages')).toBeVisible({ timeout: 15000 });

		console.log('✅ Step 2a completed: Page refreshed and conversation clicked');

		// Step 2b: Make sure conversation is loaded with at least 4 messages
		console.log('📝 Step 2b: Validating conversation loaded with messages');

		// Verify we have 4 messages (2 user + 2 AI from setup)
		await expect(page.getByTestId('message-count')).toHaveText(/\b4 messages\b/);

		// Verify the original messages are still present in the conversation (not relying on viewport visibility)
		await expect(page.getByTestId('message-list')).toContainText(setupMessage1);
		await expect(page.getByTestId('message-list')).toContainText(setupMessage2);

		console.log('✅ Step 2b completed: Conversation loaded with correct messages');

		// Step 2c: Reply and check if message is posted
		console.log('📝 Step 2c: Posting reply to existing conversation');

		const replyMessage = 'This is a reply to the existing conversation after refresh';

		const replyChatMessageInput = page.getByRole('textbox', { name: 'Type your message...' });
		const replyChatSendButton = page.getByRole('button', { name: 'Send message (Enter)' });

		// Capture current message count BEFORE sending the reply to avoid races
		const messageCountLocator = page.getByTestId('message-count');
		const getMessageCount = async (): Promise<number> => {
			const text = await messageCountLocator.textContent();
			const m = text?.match(/(\d+) messages/);
			return m ? parseInt(m[1], 10) : 0;
		};
		const initialCount = await getMessageCount();

		await replyChatMessageInput.fill(replyMessage);
		await replyChatSendButton.click();

		// Verify the reply message appears - scope to user bubble to avoid assistant echoes
		await expect(
			page.locator('.bg-blue-600 [data-testid="message-content"]').getByText(replyMessage).first()
		).toBeVisible({ timeout: 15000 });

		console.log('✅ Step 2c completed: Reply message posted successfully');

		// Step 2d: Make sure response has come back
		console.log('📝 Step 2d: Validating AI response to reply');

		// Wait for AI response to the reply - check that message count increases in phases (>= to avoid timing races)
		await expect
			.poll(async () => await getMessageCount(), {
				timeout: 20000,
				message: 'Waiting for user reply to be counted'
			})
			.toBeGreaterThanOrEqual(initialCount + 1);
		await expect
			.poll(async () => await getMessageCount(), {
				timeout: 40000,
				message: 'Waiting for AI reply to be counted'
			})
			.toBeGreaterThanOrEqual(initialCount + 2);

		// Verify no stuck states
		const creatingIndicator = page.getByText('Creating...');
		await expect(creatingIndicator).not.toBeVisible({ timeout: 15000 });

		console.log('✅ Step 2d completed: AI response received successfully');
		console.log('🎉 Test 2 completed successfully!');
	});

	test('Test 3: Error handling and edge cases', async ({ page }) => {
		console.log('🚀 Starting Test 3: Error handling validation');

		// Test empty message handling
		const sendButton = page.getByRole('button', { name: 'New Chat' });

		// Verify button is disabled when no text is entered
		await expect(sendButton).toBeDisabled();

		// Try typing and then clearing - button should become disabled again
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill('test');
		await expect(sendButton).toBeEnabled();

		await chatInput.clear();
		await expect(sendButton).toBeDisabled();

		console.log('✅ Test 3 completed: Empty message handling verified');
	});
});
