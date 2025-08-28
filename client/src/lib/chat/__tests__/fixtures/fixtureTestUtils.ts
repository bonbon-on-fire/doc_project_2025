/**
 * Fixture Test Utilities
 *
 * Provides utilities for running fixture-based SSE stream tests.
 * Implements the fixture runner pattern with support for:
 * - Sequential and parallel test execution
 * - Detailed logging and debugging
 * - Test lifecycle hooks
 * - Assertion helpers
 */

import { expect } from 'vitest';
import { SSEStreamTestHarness, type TestSnapshot } from '../streamTestUtils';
import type { SSETestFixture, SSEEventExpectation } from './sseStreamFixtures';

/**
 * Options for running fixture tests
 */
export interface FixtureTestOptions {
	/** Only run tests for these event indices */
	only?: number[];
	/** Skip tests for these event indices */
	skip?: number[];
	/** Enable verbose logging */
	verbose?: boolean;
	/** Custom harness configuration */
	harnessConfig?: {
		/** Custom message handlers to register */
		customHandlers?: any[];
	};
	/** Run expectations in parallel (faster but may affect debugging) */
	parallel?: boolean;
	/** Fail fast on first error */
	failFast?: boolean;
}

/**
 * Result of running a fixture test
 */
export interface FixtureTestResult {
	/** The fixture that was tested */
	fixture: SSETestFixture;
	/** States captured at each event */
	states: TestSnapshot[];
	/** Any errors that occurred */
	errors: Array<{
		eventIndex: number;
		error: Error;
	}>;
	/** Execution time in milliseconds */
	duration: number;
	/** Whether all validations passed */
	passed: boolean;
}

/**
 * Runs validations for a specific event expectation
 */
async function runEventExpectation(
	state: TestSnapshot,
	expectation: SSEEventExpectation,
	eventIndex: number,
	options?: FixtureTestOptions
): Promise<void> {
	// Check if this expectation should be skipped
	if (expectation.skip === true) {
		if (options?.verbose) {
			console.log(`Skipping expectation at event ${eventIndex}`);
		}
		return;
	}

	if (typeof expectation.skip === 'function' && expectation.skip(state)) {
		if (options?.verbose) {
			console.log(`Conditionally skipping expectation at event ${eventIndex}`);
		}
		return;
	}

	// Log description if verbose
	if (options?.verbose && expectation.description) {
		console.log(`\n📋 Event ${eventIndex}: ${expectation.description}`);
	}

	// Verify event type
	expect(state.eventType).toBe(expectation.eventType);

	// Run validation
	expectation.validate(state);

	// Run debug logging if provided
	if (expectation.debug) {
		expectation.debug(state);
	}
}

/**
 * Runs expectations for multiple events
 */
export async function runFixtureExpectations(
	harness: SSEStreamTestHarness,
	fixture: SSETestFixture,
	eventIndices: number[],
	options?: FixtureTestOptions
): Promise<TestSnapshot[]> {
	const states: TestSnapshot[] = [];
	const errors: Array<{ eventIndex: number; error: Error }> = [];

	if (options?.parallel) {
		// Run all events in parallel (faster but may miss sequential dependencies)
		const results = await Promise.allSettled(
			eventIndices.map(async (index) => {
				const state = await harness.processUntilEvent(index);
				if (fixture.expectations[index]) {
					await runEventExpectation(state, fixture.expectations[index], index, options);
				}
				return { index, state };
			})
		);

		// Process results
		for (const result of results) {
			if (result.status === 'fulfilled') {
				states[result.value.index] = result.value.state;
			} else {
				const index = eventIndices[results.indexOf(result)];
				errors.push({ eventIndex: index, error: result.reason });
				if (options?.failFast) {
					throw result.reason;
				}
			}
		}
	} else {
		// Run events sequentially (default)
		for (const index of eventIndices) {
			try {
				const state = await harness.processUntilEvent(index);
				states.push(state);

				// Only run expectations if they exist for this event index
				if (fixture.expectations[index]) {
					await runEventExpectation(state, fixture.expectations[index], index, options);
				}
			} catch (error) {
				errors.push({ eventIndex: index, error: error as Error });
				if (options?.failFast) {
					throw error;
				}
			}
		}
	}

	// Report errors if any
	if (errors.length > 0 && !options?.failFast) {
		console.error(`\n❌ ${errors.length} validation(s) failed:`);
		for (const { eventIndex, error } of errors) {
			console.error(`  Event ${eventIndex}: ${error.message}`);
		}
		throw new Error(`${errors.length} validation(s) failed`);
	}

	return states;
}

/**
 * Creates a test function from a fixture
 */
export function createFixtureTest(
	fixture: SSETestFixture,
	options?: FixtureTestOptions
): () => Promise<void> {
	return async () => {
		const startTime = performance.now();

		// Run setup if provided
		if (fixture.setup) {
			await fixture.setup();
		}

		try {
			// Create harness with a default user message for init processing
			const harness = new SSEStreamTestHarness(fixture.streamContent, 'Test user message');

			// Determine which event indices to test
			let eventIndices = options?.only;
			if (!eventIndices) {
				const expectationIndices = Object.keys(fixture.expectations).map(Number);

				// If no expectations are defined, process all events
				if (expectationIndices.length === 0) {
					// Process all events to reach final state for validation
					const eventCount = harness.getEvents().length;
					eventIndices = Array.from({ length: eventCount }, (_, i) => i);
				} else {
					eventIndices = expectationIndices
						.filter((i) => !options?.skip?.includes(i))
						.sort((a, b) => a - b);
				}
			}

			// Log test start if verbose
			if (options?.verbose) {
				console.log(`\n🧪 Running fixture: ${fixture.id}`);
				console.log(`📝 ${fixture.description}`);
				console.log(`🎯 Testing ${eventIndices.length} events: [${eventIndices.join(', ')}]`);
			}

			// Run expectations
			const states = await runFixtureExpectations(harness, fixture, eventIndices, options);

			// Run final validation if provided
			if (fixture.finalValidation) {
				// If we have processed states, use the last one
				// Otherwise, process all events to get final state
				let finalState: TestSnapshot;
				if (states.length > 0) {
					finalState = states[states.length - 1];
				} else {
					// No expectations were run, but we still need final state
					// Process all events to reach the end
					await harness.processAll();
					finalState = harness.getCurrentState();
				}
				fixture.finalValidation(finalState);
			}

			// Log success if verbose
			if (options?.verbose) {
				const duration = performance.now() - startTime;
				console.log(`\n✅ Fixture passed in ${duration.toFixed(2)}ms`);
			}
		} finally {
			// Run teardown if provided
			if (fixture.teardown) {
				await fixture.teardown();
			}
		}
	};
}

/**
 * Creates a batch test that runs multiple fixtures
 */
export function createBatchFixtureTest(
	fixtures: SSETestFixture[],
	options?: FixtureTestOptions
): () => Promise<FixtureTestResult[]> {
	return async () => {
		const results: FixtureTestResult[] = [];

		for (const fixture of fixtures) {
			const startTime = performance.now();
			const errors: Array<{ eventIndex: number; error: Error }> = [];
			let states: TestSnapshot[] = [];
			let passed = false;

			try {
				const test = createFixtureTest(fixture, options);
				await test();
				passed = true;
			} catch (error) {
				errors.push({ eventIndex: -1, error: error as Error });
			}

			results.push({
				fixture,
				states,
				errors,
				duration: performance.now() - startTime,
				passed
			});

			if (options?.failFast && !passed) {
				break;
			}
		}

		// Report summary
		const passed = results.filter((r) => r.passed).length;
		const failed = results.filter((r) => !r.passed).length;
		console.log(`\n📊 Batch Test Summary: ${passed} passed, ${failed} failed`);

		return results;
	};
}

/**
 * Helper to create a parameterized test from a fixture
 */
export function createParameterizedFixtureTest<T>(
	fixtureFactory: (params: T) => SSETestFixture,
	parameters: Array<{ name: string; params: T }>,
	options?: FixtureTestOptions
): Array<[string, () => Promise<void>]> {
	return parameters.map(({ name, params }) => {
		const fixture = fixtureFactory(params);
		return [name, createFixtureTest(fixture, options)];
	});
}

/**
 * Assert helpers for common validations
 */
export const FixtureAssertions = {
	/**
	 * Asserts that messages are preserved across events
	 */
	messagesPreserved(
		previousState: TestSnapshot,
		currentState: TestSnapshot,
		messageIds: string[]
	): void {
		for (const messageId of messageIds) {
			const prevMessage = previousState.chat?.messages.find((m) => m.id === messageId);
			const currMessage = currentState.chat?.messages.find((m) => m.id === messageId);

			expect(currMessage).toBeTruthy();
			expect(currMessage).toEqual(prevMessage);
		}
	},

	/**
	 * Asserts that message count has increased
	 */
	messageCountIncreased(previousState: TestSnapshot, currentState: TestSnapshot, by = 1): void {
		const prevCount = previousState.chat?.messages.length || 0;
		const currCount = currentState.chat?.messages.length || 0;
		expect(currCount).toBe(prevCount + by);
	},

	/**
	 * Asserts that streaming has transitioned correctly
	 */
	streamingTransitioned(
		previousState: TestSnapshot,
		currentState: TestSnapshot,
		fromMessageId: string | null,
		toMessageId: string | null
	): void {
		expect(previousState.streamingState.currentMessageId).toBe(fromMessageId);
		expect(currentState.streamingState.currentMessageId).toBe(toMessageId);
	}
};

/**
 * Test reporter for detailed output
 */
export class FixtureTestReporter {
	private results: FixtureTestResult[] = [];

	addResult(result: FixtureTestResult): void {
		this.results.push(result);
	}

	generateReport(): string {
		const lines: string[] = ['\n' + '='.repeat(80), 'FIXTURE TEST REPORT', '='.repeat(80)];

		for (const result of this.results) {
			lines.push('');
			lines.push(`📦 Fixture: ${result.fixture.id}`);
			lines.push(`   Description: ${result.fixture.description}`);
			lines.push(`   Status: ${result.passed ? '✅ PASSED' : '❌ FAILED'}`);
			lines.push(`   Duration: ${result.duration.toFixed(2)}ms`);

			if (result.errors.length > 0) {
				lines.push('   Errors:');
				for (const { eventIndex, error } of result.errors) {
					lines.push(`     - Event ${eventIndex}: ${error.message}`);
				}
			}
		}

		lines.push('');
		lines.push('='.repeat(80));

		const totalPassed = this.results.filter((r) => r.passed).length;
		const totalFailed = this.results.filter((r) => !r.passed).length;
		lines.push(`SUMMARY: ${totalPassed} passed, ${totalFailed} failed`);
		lines.push('='.repeat(80));

		return lines.join('\n');
	}

	printReport(): void {
		console.log(this.generateReport());
	}
}
