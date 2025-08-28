import { describe, expect, it, vi, beforeAll } from 'vitest';
import { render } from '@testing-library/svelte';
import Page from './+page.svelte';

// Mock SvelteKit modules
vi.mock('$app/navigation', () => ({
	goto: vi.fn()
}));

vi.mock('$app/environment', () => ({
	browser: true
}));

describe('/+page.svelte', () => {
	beforeAll(() => {
		// Set test mode in import.meta.env
		if (typeof import.meta !== 'undefined') {
			(import.meta as any).env = { MODE: 'test' };
		}
	});

	it('should render loading message', async () => {
		const { getByText } = render(Page);

		// Check for loading message
		const loadingText = getByText('Loading AI Chat...');
		expect(loadingText).toBeInTheDocument();

		// Check for spinner element
		const spinnerContainer = document.querySelector('.animate-spin');
		expect(spinnerContainer).toBeInTheDocument();
	});

	it('should not redirect in test mode', async () => {
		const { goto } = await import('$app/navigation');

		render(Page);

		// Wait for onMount to execute
		await new Promise((resolve) => setTimeout(resolve, 100));

		// Should not have called goto since we're in test mode
		expect(goto).not.toHaveBeenCalled();
	});
});
