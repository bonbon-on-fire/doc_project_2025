import { describe, test, expect } from 'vitest';
import { parseMarkdown } from '$lib/markdown/parse';

const baseMessage = {
	id: 'm1',
	chatId: 'c1',
	role: 'assistant',
	timestamp: new Date(),
	sequenceNumber: 1
};

describe('Markdown Parsing Utility (non-DOM environment)', () => {
	test('parses basic syntax producing expected HTML substrings', () => {
		const md = '# Title\n\n**Bold** *Italic*\n\n- item1\n- item2\n\n```js\nconsole.log(1);\n```';
		const html = parseMarkdown(md);
		expect(html).toContain('<h1');
		expect(html).toContain('Title');
		expect(html).toContain('<strong>Bold</strong>');
		expect(html).toContain('<em>Italic</em>');
		expect(html).toMatch(/<li>item1<\/li>/);
		expect(html).toMatch(/<code class="language-js"/);
	});

	test('handles malformed markdown gracefully', () => {
		const md = 'Unclosed **bold and `code';
		const html = parseMarkdown(md);
		expect(html).toContain('Unclosed');
	});
});
