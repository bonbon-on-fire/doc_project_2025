import { test, expect } from '@playwright/test';

test.describe('Task List Server-Driven Synchronization', () => {
	test.beforeEach(async ({ page }) => {
		// Capture browser console logs for debugging
		page.on('console', (msg) => {
			const type = msg.type();
			if (type === 'error' || type === 'warning') {
				console.log(`[Browser ${type}]:`, msg.text());
			}
		});

		// Navigate to the chat page
		await page.goto('http://localhost:5173/chat');
		await page.waitForLoadState('networkidle');
		await expect(page.getByPlaceholder('Start a new conversation...')).toBeVisible();
	});

	test.describe('Basic Task Operations', () => {
		test('should create tasks and update their status via server synchronization', async ({
			page
		}) => {
			console.log('📋 Testing basic task creation and status updates');

			// Step 1: Send message to create a task
			const createTaskMessage = `
<|instruction_start|>
{
  "id_message": "test-task-create",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Write E2E tests for task synchronization",
            "parentId": null
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Write E2E tests for task synchronization\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [], \\"notes\\": []}], \\"result\\": \\"Added task 1: Write E2E tests for task synchronization\\"}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Let me add a task for writing E2E tests
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createTaskMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			// Wait for navigation and message rendering
			await page.waitForURL('**/chat/**', { timeout: 10000 });
			await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });

			// Wait for the task tracker to appear
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand the task tracker if collapsed
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			const isExpanded = await taskTrackerButton.getAttribute('aria-expanded');
			if (isExpanded !== 'true') {
				await taskTrackerButton.click();
				await expect(taskTrackerButton).toHaveAttribute('aria-expanded', 'true');
			}

			// Verify task appears in the UI
			const taskList = page.locator('#task-list');
			await expect(taskList).toBeVisible();
			await expect(taskList).toContainText('Write E2E tests for task synchronization');

			// Verify task has correct initial status (NotStarted = empty checkbox)
			const taskItem = taskList.locator('div').filter({ hasText: 'Write E2E tests' }).first();
			await expect(taskItem).toContainText('□'); // NotStarted symbol

			console.log('✅ Task creation verified');

			// Step 2: Update task status to InProgress
			const updateStatusMessage = `
<|instruction_start|>
{
  "id_message": "test-task-update",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 1,
            "status": "in progress"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Write E2E tests for task synchronization\\", \\"status\\": \\"InProgress\\", \\"subtasks\\": [], \\"notes\\": []}], \\"result\\": \\"Updated task 1 status to 'in progress'.\\"}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Update the task status to in progress
			`.trim();

			// Send update message
			const messageInput = page.getByPlaceholder('Type your message...');
			await messageInput.fill(updateStatusMessage);
			await messageInput.press('Enter');

			// Wait for SSE update to be processed
			await page.waitForTimeout(2000);

			// Verify status updated in UI
			await expect(taskItem).toContainText('◻'); // InProgress symbol
			console.log('✅ Task status update to InProgress verified');

			// Step 3: Complete the task
			const completeTaskMessage = `
<|instruction_start|>
{
  "id_message": "test-task-complete",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 1,
            "status": "completed"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Write E2E tests for task synchronization\\", \\"status\\": \\"Completed\\", \\"subtasks\\": [], \\"notes\\": []}], \\"result\\": \\"Updated task 1 status to 'completed'.\\"}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Mark the task as completed
			`.trim();

			await messageInput.fill(completeTaskMessage);
			await messageInput.press('Enter');

			// Wait for SSE update
			await page.waitForTimeout(2000);

			// Verify task shows as completed
			await expect(taskItem).toContainText('☑'); // Completed symbol
			console.log('✅ Task completion verified');

			// Verify task statistics update
			const statsText = page.locator('.pinned-task-tracker').locator('text=/1 task.*1 completed/');
			await expect(statsText).toBeVisible();
			console.log('✅ Task statistics updated correctly');
		});

		test('should handle adding subtasks to existing tasks', async ({ page }) => {
			console.log('🗂️ Testing subtask creation');

			// Create a main task with subtasks
			const createTaskWithSubtasksMessage = `
<|instruction_start|>
{
  "id_message": "test-subtasks",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_bulk_initialize", 
          "args": {
            "tasks": [
              {
                "task": "Implement feature X",
                "subTasks": ["Design API", "Write code", "Add tests"],
                "notes": ["This is the main feature task"]
              }
            ]
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Implement feature X\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 2, \\"title\\": \\"Design API\\", \\"status\\": \\"NotStarted\\", \\"parentId\\": 1}, {\\"id\\": 3, \\"title\\": \\"Write code\\", \\"status\\": \\"NotStarted\\", \\"parentId\\": 1}, {\\"id\\": 4, \\"title\\": \\"Add tests\\", \\"status\\": \\"NotStarted\\", \\"parentId\\": 1}], \\"notes\\": [\\"This is the main feature task\\"]}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Create a task with subtasks for implementing feature X
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createTaskWithSubtasksMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			// Wait for navigation and task tracker
			await page.waitForURL('**/chat/**', { timeout: 10000 });
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand task tracker
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			await expect(taskTrackerButton).toHaveAttribute('aria-expanded', 'true');

			// Verify main task appears
			const taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Implement feature X');

			// Verify subtasks appear with proper indentation
			await expect(taskList).toContainText('Design API');
			await expect(taskList).toContainText('Write code');
			await expect(taskList).toContainText('Add tests');

			// Verify task note is displayed
			await expect(taskList).toContainText('This is the main feature task');

			// Verify task hierarchy (subtasks should be indented)
			const subtask = taskList.locator('div').filter({ hasText: 'Design API' }).first();
			const subtaskPaddingLeft = await subtask.evaluate(
				(el) => window.getComputedStyle(el).paddingLeft
			);
			expect(parseInt(subtaskPaddingLeft)).toBeGreaterThan(0);

			console.log('✅ Task hierarchy with subtasks verified');

			// Verify task count shows correct total
			const statsText = page.locator('.pinned-task-tracker').locator('text=/4 tasks/');
			await expect(statsText).toBeVisible();
			console.log('✅ Task count includes subtasks');
		});
	});

	test.describe('Bulk Task Operations', () => {
		test('should initialize multiple tasks at once and display them correctly', async ({
			page
		}) => {
			console.log('📚 Testing bulk task initialization');

			const bulkInitMessage = `
<|instruction_start|>
{
  "id_message": "test-bulk-init",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_bulk_initialize", 
          "args": {
            "tasks": [
              {
                "task": "Setup development environment",
                "subTasks": ["Install dependencies", "Configure IDE"],
                "notes": ["Use Node.js v20+"]
              },
              {
                "task": "Design database schema",
                "subTasks": ["Create ERD", "Define relationships"],
                "notes": ["Consider scalability", "Add proper indexes"]
              },
              {
                "task": "Implement authentication",
                "subTasks": ["JWT setup", "User registration", "Login flow"],
                "notes": []
              }
            ],
            "clearExisting": false
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Setup development environment\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 2, \\"title\\": \\"Install dependencies\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 3, \\"title\\": \\"Configure IDE\\", \\"status\\": \\"NotStarted\\"}], \\"notes\\": [\\"Use Node.js v20+\\"]}, {\\"id\\": 4, \\"title\\": \\"Design database schema\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 5, \\"title\\": \\"Create ERD\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 6, \\"title\\": \\"Define relationships\\", \\"status\\": \\"NotStarted\\"}], \\"notes\\": [\\"Consider scalability\\", \\"Add proper indexes\\"]}, {\\"id\\": 7, \\"title\\": \\"Implement authentication\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 8, \\"title\\": \\"JWT setup\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 9, \\"title\\": \\"User registration\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 10, \\"title\\": \\"Login flow\\", \\"status\\": \\"NotStarted\\"}]}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Initialize project tasks
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(bulkInitMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			// Wait for navigation
			await page.waitForURL('**/chat/**', { timeout: 10000 });
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand task tracker
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			await expect(taskTrackerButton).toHaveAttribute('aria-expanded', 'true');

			// Verify all main tasks appear
			const taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Setup development environment');
			await expect(taskList).toContainText('Design database schema');
			await expect(taskList).toContainText('Implement authentication');

			// Verify subtasks
			await expect(taskList).toContainText('Install dependencies');
			await expect(taskList).toContainText('Configure IDE');
			await expect(taskList).toContainText('Create ERD');
			await expect(taskList).toContainText('JWT setup');
			await expect(taskList).toContainText('Login flow');

			// Verify notes are displayed for tasks that have them
			await expect(taskList).toContainText('Use Node.js v20+');
			await expect(taskList).toContainText('Consider scalability');
			await expect(taskList).toContainText('Add proper indexes');

			// Verify total task count (3 main + 7 subtasks = 10)
			const statsText = page.locator('.pinned-task-tracker').locator('text=/10 tasks/');
			await expect(statsText).toBeVisible();

			console.log('✅ Bulk task initialization verified');

			// Test updating multiple tasks in sequence
			const updateMultipleMessage = `
<|instruction_start|>
{
  "id_message": "test-bulk-update",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 2,
            "status": "completed"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Setup development environment\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 2, \\"title\\": \\"Install dependencies\\", \\"status\\": \\"Completed\\"}, {\\"id\\": 3, \\"title\\": \\"Configure IDE\\", \\"status\\": \\"NotStarted\\"}], \\"notes\\": [\\"Use Node.js v20+\\"]}, {\\"id\\": 4, \\"title\\": \\"Design database schema\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 5, \\"title\\": \\"Create ERD\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 6, \\"title\\": \\"Define relationships\\", \\"status\\": \\"NotStarted\\"}], \\"notes\\": [\\"Consider scalability\\", \\"Add proper indexes\\"]}, {\\"id\\": 7, \\"title\\": \\"Implement authentication\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 8, \\"title\\": \\"JWT setup\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 9, \\"title\\": \\"User registration\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 10, \\"title\\": \\"Login flow\\", \\"status\\": \\"NotStarted\\"}]}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Mark dependencies as installed
			`.trim();

			const messageInput = page.getByPlaceholder('Type your message...');
			await messageInput.fill(updateMultipleMessage);
			await messageInput.press('Enter');

			// Wait for SSE update
			await page.waitForTimeout(2000);

			// Verify subtask status changed
			const subtaskItem = taskList
				.locator('div')
				.filter({ hasText: 'Install dependencies' })
				.first();
			await expect(subtaskItem).toContainText('☑'); // Completed symbol

			// Verify stats updated
			const updatedStats = page.locator('.pinned-task-tracker').locator('text=/1 completed/');
			await expect(updatedStats).toBeVisible();

			console.log('✅ Bulk task updates verified');
		});

		test('should clear existing tasks when specified', async ({ page }) => {
			console.log('🔄 Testing task clearing and replacement');

			// First, create initial tasks
			const initialTasksMessage = `
<|instruction_start|>
{
  "id_message": "test-initial",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Old task that will be cleared"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Old task that will be cleared\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Add initial task
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(initialTasksMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand and verify initial task
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			const taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Old task that will be cleared');

			// Now clear and replace with new tasks
			const clearAndReplaceMessage = `
<|instruction_start|>
{
  "id_message": "test-clear",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_bulk_initialize", 
          "args": {
            "tasks": [
              {
                "task": "New task after clearing",
                "subTasks": ["Fresh subtask"],
                "notes": []
              }
            ],
            "clearExisting": true
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"New task after clearing\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 2, \\"title\\": \\"Fresh subtask\\", \\"status\\": \\"NotStarted\\"}]}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Clear old tasks and add new ones
			`.trim();

			const messageInput = page.getByPlaceholder('Type your message...');
			await messageInput.fill(clearAndReplaceMessage);
			await messageInput.press('Enter');

			// Wait for SSE update
			await page.waitForTimeout(2000);

			// Verify old task is gone and new task appears
			await expect(taskList).not.toContainText('Old task that will be cleared');
			await expect(taskList).toContainText('New task after clearing');
			await expect(taskList).toContainText('Fresh subtask');

			// Verify task count reset
			const statsText = page.locator('.pinned-task-tracker').locator('text=/2 tasks/');
			await expect(statsText).toBeVisible();

			console.log('✅ Task clearing and replacement verified');
		});
	});

	test.describe('Real-time SSE Synchronization', () => {
		test('should receive and display task updates via SSE without page refresh', async ({
			page
		}) => {
			console.log('🔄 Testing real-time SSE task synchronization');

			// Create initial task
			const createTaskMessage = `
<|instruction_start|>
{
  "id_message": "test-sse-sync",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Task to update via SSE"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Task to update via SSE\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Create a task to test SSE updates
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createTaskMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			const chatId = page.url().split('/').pop();
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand task tracker
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			const taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Task to update via SSE');

			// Verify SSE connection is established (check network tab would be ideal, but we'll verify behavior)
			console.log('📡 SSE connection established for chat:', chatId);

			// Send multiple rapid updates to test SSE streaming
			const updates = [
				{ status: 'InProgress', symbol: '◻' },
				{ status: 'Completed', symbol: '☑' }
			];

			for (const update of updates) {
				const updateMessage = `
<|instruction_start|>
{
  "id_message": "test-sse-${update.status}",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 1,
            "status": "${update.status.toLowerCase()}"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Task to update via SSE\\", \\"status\\": \\"${update.status}\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Update status to ${update.status}
				`.trim();

				const messageInput = page.getByPlaceholder('Type your message...');
				await messageInput.fill(updateMessage);
				await messageInput.press('Enter');

				// Wait for SSE update to be processed
				await page.waitForTimeout(1500);

				// Verify status updated without page refresh
				const taskItem = taskList
					.locator('div')
					.filter({ hasText: 'Task to update via SSE' })
					.first();
				await expect(taskItem).toContainText(update.symbol);
				console.log(`✅ SSE update to ${update.status} received and displayed`);
			}

			// Verify task tracker persists when navigating to sidebar and back
			const sidebarChats = page.locator('[data-testid="chat-item"]');
			const firstChat = sidebarChats.first();
			await firstChat.click();
			await page.waitForTimeout(1000);

			// Task tracker should still be visible and contain our tasks
			await expect(page.locator('.pinned-task-tracker')).toBeVisible();
			await expect(taskList).toContainText('Task to update via SSE');
			console.log('✅ Task state persists across navigation');
		});

		test('should handle concurrent task operations via SSE', async ({ page }) => {
			console.log('⚡ Testing concurrent task operations');

			// Create multiple tasks in rapid succession
			const createMultipleTasksMessage = `
<|instruction_start|>
{
  "id_message": "test-concurrent",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Concurrent task 1"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Concurrent task 1\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Add first concurrent task
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createMultipleTasksMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand task tracker
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			const taskList = page.locator('#task-list');

			// Send multiple task operations without waiting between them
			const operations = [
				{
					name: 'TaskManager_add_task',
					args: { title: 'Concurrent task 2' },
					result:
						'[{\\"id\\": 1, \\"title\\": \\"Concurrent task 1\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 2, \\"title\\": \\"Concurrent task 2\\", \\"status\\": \\"NotStarted\\"}]'
				},
				{
					name: 'TaskManager_add_task',
					args: { title: 'Concurrent task 3' },
					result:
						'[{\\"id\\": 1, \\"title\\": \\"Concurrent task 1\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 2, \\"title\\": \\"Concurrent task 2\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 3, \\"title\\": \\"Concurrent task 3\\", \\"status\\": \\"NotStarted\\"}]'
				}
			];

			const messageInput = page.getByPlaceholder('Type your message...');

			// Send all operations rapidly
			for (let i = 0; i < operations.length; i++) {
				const op = operations[i];
				const message = `
<|instruction_start|>
{
  "id_message": "test-concurrent-${i + 2}",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "${op.name}", 
          "args": ${JSON.stringify(op.args)}
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": ${op.result}}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Add concurrent task ${i + 2}
				`.trim();

				await messageInput.fill(message);
				await messageInput.press('Enter');
				// Don't wait between sends to test concurrent handling
			}

			// Wait for all SSE updates to be processed
			await page.waitForTimeout(3000);

			// Verify all tasks appear in the correct order
			await expect(taskList).toContainText('Concurrent task 1');
			await expect(taskList).toContainText('Concurrent task 2');
			await expect(taskList).toContainText('Concurrent task 3');

			// Verify task count is correct
			const statsText = page.locator('.pinned-task-tracker').locator('text=/3 tasks/');
			await expect(statsText).toBeVisible();

			console.log('✅ Concurrent task operations handled correctly');

			// Test that task state remains consistent after rapid updates
			const rapidUpdateMessage = `
<|instruction_start|>
{
  "id_message": "test-rapid-update",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 1,
            "status": "completed"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Concurrent task 1\\", \\"status\\": \\"Completed\\"}, {\\"id\\": 2, \\"title\\": \\"Concurrent task 2\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 3, \\"title\\": \\"Concurrent task 3\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Complete first task
			`.trim();

			await messageInput.fill(rapidUpdateMessage);
			await messageInput.press('Enter');
			await page.waitForTimeout(2000);

			// Verify only the first task is completed
			const task1 = taskList.locator('div').filter({ hasText: 'Concurrent task 1' }).first();
			const task2 = taskList.locator('div').filter({ hasText: 'Concurrent task 2' }).first();
			const task3 = taskList.locator('div').filter({ hasText: 'Concurrent task 3' }).first();

			await expect(task1).toContainText('☑'); // Completed
			await expect(task2).toContainText('□'); // NotStarted
			await expect(task3).toContainText('□'); // NotStarted

			console.log('✅ Task state consistency maintained after rapid updates');
		});

		test('should maintain task synchronization across multiple browser tabs', async ({
			page,
			context
		}) => {
			console.log('🌐 Testing multi-tab task synchronization');

			// Create initial task in first tab
			const createTaskMessage = `
<|instruction_start|>
{
  "id_message": "test-multi-tab",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Cross-tab synchronized task"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Cross-tab synchronized task\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Create task for multi-tab test
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createTaskMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			const chatUrl = page.url();
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand task tracker in first tab
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			const taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Cross-tab synchronized task');

			// Open second tab with same chat
			const page2 = await context.newPage();
			await page2.goto(chatUrl);
			await page2.waitForLoadState('networkidle');

			// Verify task appears in second tab
			await expect(page2.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });
			const taskTrackerButton2 = page2.locator('.pinned-task-tracker button').first();
			await taskTrackerButton2.click();
			const taskList2 = page2.locator('#task-list');
			await expect(taskList2).toContainText('Cross-tab synchronized task');

			console.log('✅ Task visible in both tabs');

			// Update task in first tab
			const updateMessage = `
<|instruction_start|>
{
  "id_message": "test-tab-update",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_update_task_status", 
          "args": {
            "taskId": 1,
            "status": "in progress"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Cross-tab synchronized task\\", \\"status\\": \\"InProgress\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Update task status in first tab
			`.trim();

			const messageInput = page.getByPlaceholder('Type your message...');
			await messageInput.fill(updateMessage);
			await messageInput.press('Enter');

			// Wait for SSE updates in both tabs
			await page.waitForTimeout(2000);

			// Verify update appears in both tabs
			const task1Tab1 = taskList
				.locator('div')
				.filter({ hasText: 'Cross-tab synchronized' })
				.first();
			const task1Tab2 = taskList2
				.locator('div')
				.filter({ hasText: 'Cross-tab synchronized' })
				.first();

			await expect(task1Tab1).toContainText('◻'); // InProgress in tab 1
			await expect(task1Tab2).toContainText('◻'); // InProgress in tab 2

			console.log('✅ Task updates synchronized across tabs via SSE');

			// Clean up
			await page2.close();
		});
	});

	test.describe('Task Persistence and Navigation', () => {
		test('should persist tasks when navigating between chats', async ({ page }) => {
			console.log('🔄 Testing task persistence across navigation');

			// Create first chat with tasks
			const firstChatMessage = `
<|instruction_start|>
{
  "id_message": "test-persist-1",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Task in first chat"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Task in first chat\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Create task in first chat
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(firstChatMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			const firstChatUrl = page.url();
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand and verify first chat tasks
			let taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			let taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Task in first chat');

			// Create second chat with different tasks
			const secondChatMessage = `
<|instruction_start|>
{
  "id_message": "test-persist-2",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_add_task", 
          "args": {
            "title": "Task in second chat"
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Task in second chat\\", \\"status\\": \\"NotStarted\\"}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Create task in second chat
			`.trim();

			await input.fill(secondChatMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			await page.waitForURL('**/chat/**', { timeout: 10000 });
			const secondChatUrl = page.url();
			expect(secondChatUrl).not.toBe(firstChatUrl);

			// Verify second chat has its own tasks
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });
			taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			const isExpanded = await taskTrackerButton.getAttribute('aria-expanded');
			if (isExpanded !== 'true') {
				await taskTrackerButton.click();
			}
			taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Task in second chat');
			await expect(taskList).not.toContainText('Task in first chat');

			console.log('✅ Each chat maintains separate task list');

			// Navigate back to first chat via sidebar
			const sidebarChats = page.locator('[data-testid="chat-item"]');
			const firstChatItem = sidebarChats.filter({ hasText: /first chat/i }).first();
			await firstChatItem.click();

			await page.waitForURL(firstChatUrl);
			await page.waitForTimeout(1000);

			// Verify first chat tasks are restored
			await expect(page.locator('.pinned-task-tracker')).toBeVisible();
			taskList = page.locator('#task-list');
			await expect(taskList).toContainText('Task in first chat');
			await expect(taskList).not.toContainText('Task in second chat');

			console.log('✅ Tasks correctly restored when navigating between chats');
		});

		test('should handle task operations when chat is not yet created', async ({ page }) => {
			console.log('🆕 Testing task operations during chat creation');

			// Send a complex message that creates chat and initializes tasks in one go
			const createChatWithTasksMessage = `
<|instruction_start|>
{
  "id_message": "test-chat-create",
  "messages": [
    { 
      "tool_call": [
        {
          "name": "TaskManager_bulk_initialize", 
          "args": {
            "tasks": [
              {
                "task": "Initial task during chat creation",
                "subTasks": ["Subtask 1", "Subtask 2"],
                "notes": ["Created with chat initialization"]
              }
            ]
          }
        }
      ]
    },
    {
      "tool_result": [
        {
          "output": "{\\"tasks\\": [{\\"id\\": 1, \\"title\\": \\"Initial task during chat creation\\", \\"status\\": \\"NotStarted\\", \\"subtasks\\": [{\\"id\\": 2, \\"title\\": \\"Subtask 1\\", \\"status\\": \\"NotStarted\\"}, {\\"id\\": 3, \\"title\\": \\"Subtask 2\\", \\"status\\": \\"NotStarted\\"}], \\"notes\\": [\\"Created with chat initialization\\"]}]}"
        }
      ]
    }
  ]
}
<|instruction_end|>
Initialize tasks while creating new chat
			`.trim();

			const input = page.getByPlaceholder('Start a new conversation...');
			await input.fill(createChatWithTasksMessage);
			await page.getByRole('button', { name: 'New Chat' }).click();

			// Wait for chat creation and navigation
			await page.waitForURL('**/chat/**', { timeout: 10000 });

			// Verify task tracker appears immediately
			await expect(page.locator('.pinned-task-tracker')).toBeVisible({ timeout: 15000 });

			// Expand and verify tasks
			const taskTrackerButton = page.locator('.pinned-task-tracker button').first();
			await taskTrackerButton.click();
			const taskList = page.locator('#task-list');

			await expect(taskList).toContainText('Initial task during chat creation');
			await expect(taskList).toContainText('Subtask 1');
			await expect(taskList).toContainText('Subtask 2');
			await expect(taskList).toContainText('Created with chat initialization');

			// Verify task count
			const statsText = page.locator('.pinned-task-tracker').locator('text=/3 tasks/');
			await expect(statsText).toBeVisible();

			console.log('✅ Tasks initialized correctly during chat creation');

			// Verify chat appears in sidebar with correct title
			const sidebarChats = page.locator('[data-testid="chat-item"]');
			await expect(sidebarChats.first()).toBeVisible();

			console.log('✅ Chat created successfully with tasks');
		});
	});
});
