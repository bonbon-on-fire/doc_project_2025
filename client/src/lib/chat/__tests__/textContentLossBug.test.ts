import { describe, test, expect } from 'vitest';
import { SSEStreamTestHarness } from './streamTestUtils';
import { SSE_FIXTURES, getFixturesByTag } from './fixtures/sseStreamFixtures';
import {
	createFixtureTest,
	createBatchFixtureTest,
	FixtureAssertions,
	type FixtureTestOptions
} from './fixtures/fixtureTestUtils';

// =============================================================================
// FIXTURE-BASED TESTS
// =============================================================================

describe('Text Content Loss Bug - Fixture-Based Tests', () => {
	// Test options for verbose output during debugging
	const testOptions: FixtureTestOptions = {
		verbose: process.env.VERBOSE === 'true',
		failFast: false
	};
	// -------------------------------------------------------------------------
	// Test: Critical bug reproduction
	// -------------------------------------------------------------------------
	test(
		'CRITICAL BUG: text message loses content when tools message starts',
		createFixtureTest(SSE_FIXTURES.TEXT_THEN_TOOLS, testOptions)
	);

	// -------------------------------------------------------------------------
	// Test: Verify text message structure at specific point
	// -------------------------------------------------------------------------
	test(
		'shows that text message placeholder has correct structure after streaming',
		createFixtureTest(SSE_FIXTURES.TEXT_THEN_TOOLS, {
			...testOptions,
			only: [1], // Only run validation for event index 1
			verbose: true // Always verbose for this detailed analysis test
		})
	);

	// -------------------------------------------------------------------------
	// Test: Step-by-step progression through events
	// -------------------------------------------------------------------------
	test(
		'validates state at each critical event transition',
		createFixtureTest(SSE_FIXTURES.TEXT_THEN_TOOLS, {
			...testOptions,
			verbose: true, // Show detailed progression
			failFast: false // Continue testing even if one event fails
		})
	);
});

// =============================================================================
// BATCH TESTS FOR REGRESSION TESTING
// =============================================================================

describe('Text Content Preservation - Regression Suite', () => {
	test(
		'all text preservation fixtures should pass',
		createBatchFixtureTest(getFixturesByTag('text-preservation'), { failFast: false })
	);

	test(
		'bug reproduction fixtures should identify issues',
		createBatchFixtureTest(getFixturesByTag('bug'), { verbose: true })
	);
});

// =============================================================================
// STATE TRANSITION TESTS
// =============================================================================

describe('Message State Transitions', () => {
	test('messages should be preserved across streaming transitions', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TEXT_THEN_TOOLS.streamContent);

		// Capture states at key transitions
		const state0 = await harness.processUntilEvent(0);
		const state1 = await harness.processUntilEvent(1);
		const state2 = await harness.processUntilEvent(2);

		// Verify message preservation across transitions
		FixtureAssertions.messageCountIncreased(state0, state1, 1);
		FixtureAssertions.messagesPreserved(state1, state2, ['gen-1756406547:text']);

		// Verify streaming transitions
		FixtureAssertions.streamingTransitioned(state0, state1, null, 'gen-1756406547:text');
	});
});
