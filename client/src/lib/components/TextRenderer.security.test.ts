import { describe, test, expect } from 'vitest';
import { parseMarkdown } from '$lib/markdown/parse';

const baseMessage = {
	id: 'm-sec',
	chatId: 'c1',
	role: 'assistant',
	timestamp: new Date(),
	sequenceNumber: 1
};

describe('Markdown Sanitization Security', () => {
	test('removes script tags and event handlers', () => {
		const md = '<script>alert(1)</script><img src=x onerror=alert(2)>Safe';
		const html = parseMarkdown(md);
		expect(html).not.toContain('<script');
		expect(html).not.toContain('onerror');
		expect(html).toContain('Safe');
	});

	test('sanitizes javascript: links', () => {
		const md = '[x](javascript:alert(1))';
		const html = parseMarkdown(md);
		const hrefMatch = html.match(/href="([^"]+)"/);
		if (hrefMatch) {
			expect(hrefMatch[1]).not.toMatch(/^javascript:/i);
		}
	});
});
