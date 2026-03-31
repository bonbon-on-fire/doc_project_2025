import { page } from '@vitest/browser/context';
import { describe, expect, it } from 'vitest';
import { render } from 'vitest-browser-svelte';
import Page from './+page.svelte';

describe('/+page.svelte', () => {
	it('should render loading message', async () => {
		render(Page);

		const loadingText = page.getByText('Loading AI Chat...');
		await expect.element(loadingText).toBeInTheDocument();
	});
});
