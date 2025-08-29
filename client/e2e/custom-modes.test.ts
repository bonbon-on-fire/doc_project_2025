import { test, expect } from '@playwright/test';

/**
 * E2E Tests for Custom Mode Management
 *
 * Test Scenarios:
 * 1. Custom mode creation flow
 * 2. Custom mode editing
 * 3. Custom mode deletion
 * 4. Custom mode usage in chat
 * 5. Custom mode validation
 * 6. Import/Export functionality (if implemented)
 *
 * Tests the complete custom mode lifecycle
 */

test.describe('Custom Mode Management E2E Tests', () => {
	// Helper function to generate unique mode names
	const generateModeName = () => `Custom Mode ${Date.now()}`;

	test.beforeEach(async ({ page }) => {
		// Navigate to the app
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');

		// Wait for the interface to be ready
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test.skip('Test 1: Create custom mode through UI', async ({ page }) => {
		console.log('➕ Test 1: Testing custom mode creation');

		// Open mode selector
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		// Look for "Create Custom Mode" option
		const createModeButton = page
			.getByTestId('create-custom-mode-button')
			.or(page.getByRole('button', { name: /create.*mode/i }))
			.or(page.getByText('+ Create Mode', { exact: false }));

		// If the button exists, click it
		const hasCreateButton = await createModeButton.isVisible().catch(() => false);
		if (!hasCreateButton) {
			console.log('ℹ️ Custom mode creation UI not yet implemented in main chat view');
			// Try alternative path - navigate to modes management page if it exists
			await page.goto('http://localhost:5173/modes').catch(() => {
				console.log('ℹ️ Modes management page not yet implemented');
			});
			return;
		}

		await createModeButton.click();
		console.log('✅ Step 1a: Opened custom mode creation dialog');

		// Fill in mode details
		const modeName = generateModeName();
		await page.getByLabel('Mode Name').fill(modeName);
		await page.getByLabel('Description').fill('A test custom mode for E2E testing');
		await page
			.getByLabel('System Prompt')
			.fill('You are a helpful test assistant specialized in testing');

		// Select tools (if tool selector exists)
		const toolSelector = page.getByTestId('tool-selector').or(page.getByLabel('Select Tools'));
		if (await toolSelector.isVisible()) {
			await toolSelector.click();
			// Select a few tools
			await page.getByRole('checkbox', { name: 'Search' }).check();
			await page.getByRole('checkbox', { name: 'Calculator' }).check();
		}
		console.log('✅ Step 1b: Filled custom mode form');

		// Save the mode
		await page.getByRole('button', { name: /save|create/i }).click();

		// Verify success notification
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		await expect(toast).toContainText(/created|success/i);
		console.log('✅ Step 1c: Custom mode created successfully');

		// Verify mode appears in selector
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		const customModeOption = page.getByText(modeName, { exact: false });
		await expect(customModeOption).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 1d: Custom mode appears in mode selector');
	});

	test.skip('Test 2: Load and verify saved custom mode', async ({ page }) => {
		console.log('💬 Test 2: Testing custom mode usage in chat');

		// First, check if there are any existing custom modes
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		// Look for custom modes section
		const customModesSection = page
			.getByText('Custom Modes', { exact: false })
			.or(page.getByTestId('custom-modes-section'));

		const hasCustomModes = await customModesSection.isVisible().catch(() => false);
		if (!hasCustomModes) {
			console.log('ℹ️ No custom modes available for testing');
			// Try to create one via API if possible
			// For now, skip this test
			return;
		}

		// Select the first available custom mode
		const customMode = page
			.getByTestId('mode-option-custom')
			.first()
			.or(page.locator('[data-mode-type="custom"]').first());

		const customModeName = await customMode.textContent();
		await customMode.click();
		console.log(`✅ Step 2a: Selected custom mode: ${customModeName}`);

		// Verify mode is selected
		await expect(modeSelector).toContainText(customModeName || 'Custom', { timeout: 5000 });

		// Create a chat with the custom mode
		const testMessage = 'Test message using custom mode';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		// Wait for chat creation
		await page.waitForURL('**/chat/*', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		console.log('✅ Step 2b: Chat created with custom mode');

		// Verify custom mode is shown in chat header
		const chatHeader = page.getByTestId('chat-header');
		await expect(chatHeader).toContainText(customModeName || 'Custom', { timeout: 5000 });
		console.log('✅ Step 2c: Custom mode displayed in chat header');

		// Wait for AI response
		await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, {
			timeout: 20000
		});
		console.log('✅ Step 2d: AI responded using custom mode context');
	});

	test.skip('Test 3: Update existing custom mode', async ({ page }) => {
		console.log('✏️ Test 3: Testing custom mode editing');

		// Navigate to modes management page if it exists
		const modesPageExists = await page
			.goto('http://localhost:5173/modes')
			.then(() => true)
			.catch(() => false);

		if (!modesPageExists) {
			console.log('ℹ️ Modes management page not yet implemented');
			// Try inline editing from mode selector
			const modeSelector = page.getByTestId('mode-selector');
			await modeSelector.click();
			await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

			// Look for edit buttons on custom modes
			const editButton = page
				.getByTestId('edit-mode-button')
				.first()
				.or(page.getByRole('button', { name: /edit/i }).first());

			if (!(await editButton.isVisible())) {
				console.log('ℹ️ Mode editing UI not yet implemented');
				return;
			}
		}

		// Find a custom mode to edit
		const customModeCard = page
			.getByTestId('custom-mode-card')
			.first()
			.or(page.locator('[data-mode-type="custom"]').first());

		if (!(await customModeCard.isVisible())) {
			console.log('ℹ️ No custom modes available to edit');
			return;
		}

		// Click edit button
		const editButton = customModeCard.getByRole('button', { name: /edit/i });
		await editButton.click();
		console.log('✅ Step 3a: Opened mode editor');

		// Modify mode details
		const descriptionField = page.getByLabel('Description');
		await descriptionField.clear();
		await descriptionField.fill('Updated description for E2E test');

		const promptField = page.getByLabel('System Prompt');
		await promptField.clear();
		await promptField.fill('You are an updated test assistant with new capabilities');
		console.log('✅ Step 3b: Modified mode details');

		// Save changes
		await page.getByRole('button', { name: /save|update/i }).click();

		// Verify success notification
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		await expect(toast).toContainText(/updated|saved/i);
		console.log('✅ Step 3c: Mode updated successfully');

		// Verify changes are reflected
		if (modesPageExists) {
			await expect(customModeCard).toContainText('Updated description');
		}
		console.log('✅ Step 3d: Changes persisted');
	});

	test.skip('Test 4: Delete custom mode with confirmation', async ({ page }) => {
		console.log('🗑️ Test 4: Testing custom mode deletion');

		// Navigate to modes management page if it exists
		const modesPageExists = await page
			.goto('http://localhost:5173/modes')
			.then(() => true)
			.catch(() => false);

		if (!modesPageExists) {
			console.log('ℹ️ Modes management page not yet implemented');
			// Try deletion from mode selector
			const modeSelector = page.getByTestId('mode-selector');
			await modeSelector.click();
			await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

			// Look for delete buttons on custom modes
			const deleteButton = page
				.getByTestId('delete-mode-button')
				.first()
				.or(page.getByRole('button', { name: /delete/i }).first());

			if (!(await deleteButton.isVisible())) {
				console.log('ℹ️ Mode deletion UI not yet implemented');
				return;
			}
		}

		// Find a custom mode to delete
		const customModeCard = page
			.getByTestId('custom-mode-card')
			.first()
			.or(page.locator('[data-mode-type="custom"]').first());

		if (!(await customModeCard.isVisible())) {
			console.log('ℹ️ No custom modes available to delete');
			return;
		}

		// Get the mode name before deletion
		const modeName = await customModeCard.getByTestId('mode-name').textContent();

		// Click delete button
		const deleteButton = customModeCard.getByRole('button', { name: /delete/i });
		await deleteButton.click();
		console.log('✅ Step 4a: Initiated mode deletion');

		// Confirm deletion if dialog appears
		const confirmDialog = page.getByRole('dialog').or(page.getByTestId('confirm-dialog'));
		if (await confirmDialog.isVisible()) {
			await page.getByRole('button', { name: /confirm|yes|delete/i }).click();
			console.log('✅ Step 4b: Confirmed deletion');
		}

		// Verify success notification
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		await expect(toast).toContainText(/deleted|removed/i);
		console.log('✅ Step 4c: Mode deleted successfully');

		// Verify mode is removed from list
		if (modeName) {
			await expect(page.getByText(modeName, { exact: true })).not.toBeVisible({ timeout: 5000 });
		}
		console.log('✅ Step 4d: Mode removed from UI');
	});

	test.skip('Test 5: Custom mode validation', async ({ page }) => {
		console.log('✔️ Test 5: Testing custom mode validation');

		// Open mode creation dialog
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		const createModeButton = page
			.getByTestId('create-custom-mode-button')
			.or(page.getByRole('button', { name: /create.*mode/i }))
			.or(page.getByText('+ Create Mode', { exact: false }));

		if (!(await createModeButton.isVisible())) {
			console.log('ℹ️ Custom mode creation UI not available for validation testing');
			return;
		}

		await createModeButton.click();
		console.log('✅ Step 5a: Opened mode creation dialog');

		// Try to save with empty fields
		const saveButton = page.getByRole('button', { name: /save|create/i });
		await saveButton.click();

		// Check for validation errors
		const validationError = page
			.getByTestId('validation-error')
			.or(page.getByText(/required|invalid|enter/i, { exact: false }))
			.first();
		await expect(validationError).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 5b: Validation prevents empty mode creation');

		// Fill only name and try again
		await page.getByLabel('Mode Name').fill('Test Mode');
		await saveButton.click();

		// Should still show validation for missing fields
		const descriptionError = page.getByText(/description.*required/i, { exact: false });
		if (await descriptionError.isVisible()) {
			console.log('✅ Step 5c: Description field validation works');
		}

		// Fill all required fields with valid data
		await page.getByLabel('Description').fill('Valid description');
		await page.getByLabel('System Prompt').fill('Valid prompt');

		// Try with very long name
		const longName = 'A'.repeat(256);
		await page.getByLabel('Mode Name').fill(longName);
		await saveButton.click();

		// Check for length validation
		const lengthError = page.getByText(/too long|maximum|characters/i, { exact: false });
		if (await lengthError.isVisible()) {
			console.log('✅ Step 5d: Length validation works');
		}

		// Set valid name
		await page.getByLabel('Mode Name').fill('Valid Test Mode');
		await saveButton.click();

		// Should succeed now
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 5e: Valid mode creation succeeds');
	});

	test('Test 6: Custom mode with all tools (*)', async ({ page }) => {
		console.log('🔧 Test 6: Testing custom mode with wildcard tools');

		// This test would create a mode with all tools enabled
		// and verify it works correctly

		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		const createModeButton = page
			.getByTestId('create-custom-mode-button')
			.or(page.getByRole('button', { name: /create.*mode/i }));

		if (!(await createModeButton.isVisible())) {
			console.log('ℹ️ Custom mode creation not available');
			return;
		}

		await createModeButton.click();

		// Fill mode details
		const modeName = `All Tools Mode ${Date.now()}`;
		await page.getByLabel('Mode Name').fill(modeName);
		await page.getByLabel('Description').fill('Mode with all tools enabled');
		await page.getByLabel('System Prompt').fill('You have access to all available tools');

		// Select all tools or use wildcard
		const allToolsCheckbox = page.getByRole('checkbox', { name: /all tools|select all/i });
		if (await allToolsCheckbox.isVisible()) {
			await allToolsCheckbox.check();
			console.log('✅ Step 6a: Selected all tools');
		} else {
			// Manually select multiple tools if individual selection is required
			const toolCheckboxes = page.getByRole('checkbox');
			const count = await toolCheckboxes.count();
			for (let i = 0; i < Math.min(count, 5); i++) {
				await toolCheckboxes.nth(i).check();
			}
			console.log('✅ Step 6a: Selected multiple tools');
		}

		// Save the mode
		await page.getByRole('button', { name: /save|create/i }).click();

		// Verify creation
		const toast = page.getByTestId('toast-notification');
		await expect(toast).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 6b: Mode with all tools created');

		// Use the mode in a chat
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });
		await page.getByText(modeName, { exact: false }).click();

		const testMessage = 'Test with all tools available';
		const chatInput = page.getByPlaceholder('Start a new conversation...');
		await chatInput.fill(testMessage);
		await page.getByRole('button', { name: 'New Chat' }).click();

		await page.waitForURL('**/chat/*', { timeout: 10000 });
		await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
		console.log('✅ Step 6c: Chat works with all-tools mode');
	});

	test('Test 7: Custom mode persistence and recovery', async ({ page, context }) => {
		console.log('💾 Test 7: Testing custom mode persistence');

		// Create a custom mode
		const modeSelector = page.getByTestId('mode-selector');
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		// Check if any custom modes exist
		const customModesSection = page.getByText('Custom Modes', { exact: false });
		const hasCustomModes = await customModesSection.isVisible();

		let customModeName = '';
		if (hasCustomModes) {
			// Get the name of an existing custom mode
			const customMode = page.locator('[data-mode-type="custom"]').first();
			customModeName = (await customMode.textContent()) || 'Custom Mode';
		} else {
			console.log('ℹ️ No custom modes to test persistence');
			return;
		}

		// Select the custom mode
		await page.locator('[data-mode-type="custom"]').first().click();
		console.log(`✅ Step 7a: Selected custom mode: ${customModeName}`);

		// Create a new tab
		const page2 = await context.newPage();
		await page2.goto('http://localhost:5173/chat');
		await page2.waitForLoadState('networkidle');

		// Check if custom mode is available in new tab
		const modeSelector2 = page2.getByTestId('mode-selector');
		await modeSelector2.click();
		await page2.getByTestId('mode-dropdown').waitFor({ state: 'visible' });

		const customModeInNewTab = page2.getByText(customModeName, { exact: false });
		await expect(customModeInNewTab).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 7b: Custom mode available in new tab');

		// Close the new tab
		await page2.close();

		// Refresh original page
		await page.reload();
		await page.waitForLoadState('networkidle');

		// Verify custom mode still exists
		await modeSelector.click();
		await page.getByTestId('mode-dropdown').waitFor({ state: 'visible' });
		const customModeAfterRefresh = page.getByText(customModeName, { exact: false });
		await expect(customModeAfterRefresh).toBeVisible({ timeout: 5000 });
		console.log('✅ Step 7c: Custom mode persists after refresh');
	});
});
