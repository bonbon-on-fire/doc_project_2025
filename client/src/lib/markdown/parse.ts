import { marked } from 'marked';
import createDOMPurify from 'dompurify';
// Lazy JSDOM usage for Node/SSR test environment (vitest server project)
let jsdomWindow: any | undefined;

// Configure marked once (CommonMark + GFM features)
marked.use({
	gfm: true,
	breaks: false
});

// Allowed protocols for href/src attributes
const ALLOWED_URI_REGEXP = /^(?:https?:|mailto:|tel:|\/)/i;

/**
 * Parse markdown to HTML then sanitize it to prevent XSS.
 * Returns safe HTML string ready for {@html} insertion.
 */
export function parseMarkdown(markdown: string, opts?: { devLogging?: boolean }): string {
	if (!markdown) return '';

	const rawHtml = marked.parse(markdown, { async: false }) as string;

	// Acquire a window for DOMPurify. In Node (vitest server env), spin up a lightweight JSDOM.
	let windowRef: any = typeof window !== 'undefined' ? window : undefined;
	if (!windowRef || !windowRef.document) {
		// Lazy import jsdom only when necessary to keep runtime light in browser.
		if (!jsdomWindow) {
			try {
				// Use dynamic import to avoid bundling Node-only deps in browser build
				// @ts-ignore
				const jsdomMod = (globalThis as any).require
					? (globalThis as any).require('jsdom')
					: undefined;
				if (jsdomMod && jsdomMod.JSDOM) {
					jsdomWindow = new jsdomMod.JSDOM('<!DOCTYPE html><html><body></body></html>').window;
				} else {
					// Fallback minimal sanitation via regex (no DOM APIs)
					return (
						rawHtml
							.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '')
							// Remove inline event handlers (quoted and unquoted)
							.replace(/\s+on[a-z]+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '')
							.replace(/href=("|')javascript:[^"']*("|')/gi, 'href="#"')
					);
				}
			} catch {
				// Fallback minimal sanitation via regex (no DOM APIs)
				return (
					rawHtml
						.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '')
						// Remove inline event handlers (quoted and unquoted)
						.replace(/\s+on[a-z]+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '')
						.replace(/href=("|')javascript:[^"']*("|')/gi, 'href="#"')
				);
			}
		}
		windowRef = jsdomWindow;
	}

	// createDOMPurify expects a Window-like; cast to any to accommodate jsdom window
	const DOMPurify: any = createDOMPurify(windowRef as any);

	let removedNodes = 0;
	let removedAttrs = 0;
	const enableLogging = opts?.devLogging && (import.meta as any).env?.DEV;

	// Use supported hooks; afterSanitizeAttributes can approximate removed attr logging
	if (enableLogging) {
		DOMPurify.addHook('afterSanitizeAttributes', (node: Element) => {
			if ((node as any)._removed) removedNodes++;
		});
	}

	DOMPurify.addHook('uponSanitizeAttribute', (_node: Element, data: any) => {
		// Strip javascript: style protocols for href/src
		if ((data.attrName === 'href' || data.attrName === 'src') && data.attrValue) {
			if (!ALLOWED_URI_REGEXP.test(data.attrValue)) {
				// Replace with safe placeholder instead of keeping dangerous value
				data.attrValue = '#';
				removedAttrs++;
			}
		}
		// Remove event handlers
		if (data.attrName && data.attrName.toLowerCase().startsWith('on')) {
			data.keepAttr = false;
			removedAttrs++;
		}
		// Basic style scrubbing: remove url(javascript:...) patterns
		if (data.attrName === 'style' && /javascript:/i.test(data.attrValue)) {
			data.keepAttr = false;
			removedAttrs++;
		}
	});

	const clean = DOMPurify.sanitize(rawHtml, {
		USE_PROFILES: { html: true },
		FORBID_TAGS: ['script', 'iframe', 'object', 'embed']
	});

	// Fallback defensive stripping (should be redundant, but ensures tests pass if hooks/config missed)
	let finalHtml = clean
		// Remove any lingering script tags
		.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '')
		// Remove inline event handlers
		.replace(/\s+on[a-z]+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '')
		// Neutralize javascript: protocols that might slip through
		.replace(/href=("|')javascript:[^"']*("|')/gi, 'href="#"');

	if (/javascript:/i.test(finalHtml)) {
		finalHtml = finalHtml.replace(/javascript:/gi, '');
	}

	if (enableLogging) {
		// eslint-disable-next-line no-console
		console.debug('[MarkdownSanitize]', { removedNodes, removedAttrs });
	}

	return finalHtml;
}
