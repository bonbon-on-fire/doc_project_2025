<script lang="ts">
	import { parseMarkdown } from '$lib/markdown/parse';
	import { onMount } from 'svelte';

	export let content: string = '';
	export let theme: 'light' | 'dark' | 'auto' | 'user' = 'auto';
	export let size: 'sm' | 'base' | 'lg' = 'base';

	let htmlContent = '';
	let mounted = false;

	// Parse markdown content to safe HTML
	$: if (content) {
		htmlContent = parseMarkdown(content, { devLogging: false });
	}

	onMount(() => {
		mounted = true;
		// Load KaTeX CSS for equation rendering if needed
		loadMathSupport();
	});

	async function loadMathSupport() {
		// Check if content contains math expressions
		if (content.includes('$$') || content.includes('\\(') || content.includes('\\[')) {
			try {
				// Dynamically import KaTeX CSS if not already loaded
				if (!document.querySelector('link[href*="katex"]')) {
					const link = document.createElement('link');
					link.rel = 'stylesheet';
					link.href = 'https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/katex.min.css';
					link.integrity =
						'sha384-GvrOXuhMATgEsSwCs4smul74iXGOixntILdUW9XmUC6+HX0sLNAK3q71HotJqlAn';
					link.crossOrigin = 'anonymous';
					document.head.appendChild(link);
				}
			} catch (error) {
				console.warn('Failed to load KaTeX CSS:', error);
			}
		}
	}
</script>

<div
	class="markdown-renderer {size === 'sm' ? 'text-sm' : size === 'lg' ? 'text-lg' : 'text-base'}"
	class:theme-light={theme === 'light'}
	class:theme-dark={theme === 'dark'}
	class:theme-auto={theme === 'auto'}
	class:theme-user={theme === 'user'}
>
	{@html htmlContent}
</div>

<style>
	/* Base markdown styles with elegant typography */
	.markdown-renderer {
		max-width: none;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Inter', sans-serif;
		line-height: 1.7;
		color: inherit;
	}

	/* Typography hierarchy with elegant spacing */
	.markdown-renderer :global(h1) {
		font-size: 1.5rem;
		font-weight: 700;
		margin-bottom: 1.5rem;
		margin-top: 2rem;
		color: #111827;
		line-height: 1.3;
		border-bottom: 2px solid #e5e7eb;
		padding-bottom: 0.5rem;
	}

	.markdown-renderer :global(h2) {
		font-size: 1.25rem;
		font-weight: 600;
		margin-bottom: 1rem;
		margin-top: 1.5rem;
		color: #1f2937;
		line-height: 1.35;
	}

	.markdown-renderer :global(h3) {
		font-size: 1.125rem;
		font-weight: 500;
		margin-bottom: 0.75rem;
		margin-top: 1.25rem;
		color: #1f2937;
		line-height: 1.4;
	}

	.markdown-renderer :global(h4),
	.markdown-renderer :global(h5),
	.markdown-renderer :global(h6) {
		font-size: 1rem;
		font-weight: 500;
		margin-bottom: 0.5rem;
		margin-top: 1rem;
		color: #1f2937;
	}

	/* Beautiful paragraph spacing and typography */
	.markdown-renderer :global(p) {
		margin-bottom: 1rem;
		color: #374151;
		line-height: 1.7;
		font-size: 0.95rem;
	}

	.markdown-renderer :global(p:last-child) {
		margin-bottom: 0;
	}

	/* Elegant lists with proper spacing */
	.markdown-renderer :global(ul),
	.markdown-renderer :global(ol) {
		margin-bottom: 1rem;
		padding-left: 1.5rem;
	}

	.markdown-renderer :global(ul) {
		list-style-type: disc;
	}

	.markdown-renderer :global(ol) {
		list-style-type: decimal;
	}

	.markdown-renderer :global(li) {
		margin-bottom: 0.25rem;
		color: #374151;
		line-height: 1.6;
	}

	.markdown-renderer :global(li > p) {
		margin-bottom: 0.5rem;
	}

	.markdown-renderer :global(li:last-child) {
		margin-bottom: 0;
	}

	/* Nested list styling */
	.markdown-renderer :global(ul ul),
	.markdown-renderer :global(ol ol),
	.markdown-renderer :global(ul ol),
	.markdown-renderer :global(ol ul) {
		margin-top: 0.5rem;
		margin-bottom: 0;
	}

	/* Beautiful blockquotes */
	.markdown-renderer :global(blockquote) {
		border-left: 4px solid #3b82f6;
		padding-left: 1rem;
		padding-top: 0.5rem;
		padding-bottom: 0.5rem;
		margin: 1rem 0;
		background-color: #f9fafb;
		font-style: italic;
		position: relative;
	}

	.markdown-renderer :global(blockquote p) {
		color: #4b5563;
		margin-bottom: 0.5rem;
	}

	.markdown-renderer :global(blockquote p:last-child) {
		margin-bottom: 0;
	}

	/* Elegant code blocks */
	.markdown-renderer :global(pre) {
		background-color: #111827;
		color: #f9fafb;
		border-radius: 0.5rem;
		padding: 1rem;
		margin: 1rem 0;
		overflow-x: auto;
		font-family: 'SF Mono', 'Monaco', 'Inconsolata', 'Roboto Mono', 'Consolas', monospace;
		font-size: 0.875rem;
		line-height: 1.5;
		border: 1px solid #374151;
	}

	.markdown-renderer :global(pre code) {
		background-color: transparent;
		color: inherit;
		padding: 0;
		border-radius: 0;
		font-size: 0.875rem;
		font-family: inherit;
	}

	/* Inline code with subtle styling */
	.markdown-renderer :global(code) {
		background-color: #f3f4f6;
		color: #1f2937;
		padding: 0.125rem 0.375rem;
		border-radius: 0.25rem;
		font-size: 0.875rem;
		font-family: 'SF Mono', 'Monaco', 'Inconsolata', 'Roboto Mono', 'Consolas', monospace;
	}

	/* Beautiful tables */
	.markdown-renderer :global(table) {
		width: 100%;
		border-collapse: collapse;
		margin: 1rem 0;
		font-size: 0.875rem;
		border: 1px solid #e5e7eb;
		border-radius: 0.5rem;
		overflow: hidden;
	}

	.markdown-renderer :global(thead) {
		background-color: #f9fafb;
	}

	.markdown-renderer :global(th) {
		padding: 0.75rem 1rem;
		text-align: left;
		font-weight: 600;
		color: #111827;
		border-bottom: 1px solid #d1d5db;
	}

	.markdown-renderer :global(td) {
		padding: 0.75rem 1rem;
		color: #374151;
		border-bottom: 1px solid #d1d5db;
	}

	.markdown-renderer :global(tbody tr:last-child td) {
		border-bottom: 0;
	}

	.markdown-renderer :global(tbody tr:nth-child(even)) {
		background-color: #f9fafb;
	}

	/* Links with elegant hover effects */
	.markdown-renderer :global(a) {
		color: #2563eb;
		text-decoration: none;
		transition: color 0.2s ease;
		border-bottom: 1px solid transparent;
	}

	.markdown-renderer :global(a:hover) {
		color: #1d4ed8;
		border-bottom-color: currentColor;
	}

	/* Emphasis styling */
	.markdown-renderer :global(em) {
		font-style: italic;
		color: #374151;
	}

	.markdown-renderer :global(strong) {
		font-weight: 600;
		color: #111827;
	}

	/* Horizontal rules */
	.markdown-renderer :global(hr) {
		margin: 2rem 0;
		border: 0;
		border-top: 1px solid #d1d5db;
	}

	/* Math equation styling */
	.markdown-renderer :global(.katex) {
		font-size: 1.1em;
	}

	.markdown-renderer :global(.katex-display) {
		margin: 1rem 0;
		text-align: center;
	}

	/* Task lists (checkboxes) */
	.markdown-renderer :global(ul li.task-list-item) {
		list-style: none;
		position: relative;
		padding-left: 0;
	}

	.markdown-renderer :global(ul li.task-list-item input[type='checkbox']) {
		position: absolute;
		left: 0;
		top: 0.25rem;
		margin-top: 0.125rem;
		margin-right: 0.5rem;
	}

	.markdown-renderer :global(ul li.task-list-item label) {
		padding-left: 1.5rem;
	}

	/* Images with elegant styling */
	.markdown-renderer :global(img) {
		max-width: 100%;
		height: auto;
		border-radius: 0.5rem;
		box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
		margin: 1rem 0;
	}

	/* Dark theme styles */
	:global(.dark) .markdown-renderer :global(h1) {
		color: #f9fafb;
		border-bottom-color: #374151;
	}

	:global(.dark) .markdown-renderer :global(h2),
	:global(.dark) .markdown-renderer :global(h3),
	:global(.dark) .markdown-renderer :global(h4),
	:global(.dark) .markdown-renderer :global(h5),
	:global(.dark) .markdown-renderer :global(h6) {
		color: #e5e7eb;
	}

	:global(.dark) .markdown-renderer :global(p) {
		color: #d1d5db;
	}

	:global(.dark) .markdown-renderer :global(li) {
		color: #d1d5db;
	}

	:global(.dark) .markdown-renderer :global(blockquote) {
		background-color: rgba(31, 41, 55, 0.5);
	}

	:global(.dark) .markdown-renderer :global(blockquote p) {
		color: #9ca3af;
	}

	:global(.dark) .markdown-renderer :global(code) {
		background-color: #1f2937;
		color: #e5e7eb;
	}

	:global(.dark) .markdown-renderer :global(pre) {
		background-color: #0f172a;
	}

	:global(.dark) .markdown-renderer :global(table) {
		border-color: #374151;
	}

	:global(.dark) .markdown-renderer :global(thead) {
		background-color: #1f2937;
	}

	:global(.dark) .markdown-renderer :global(th) {
		color: #f9fafb;
		border-bottom-color: #4b5563;
	}

	:global(.dark) .markdown-renderer :global(td) {
		color: #d1d5db;
		border-bottom-color: #4b5563;
	}

	:global(.dark) .markdown-renderer :global(tbody tr:nth-child(even)) {
		background-color: rgba(31, 41, 55, 0.3);
	}

	:global(.dark) .markdown-renderer :global(a) {
		color: #60a5fa;
	}

	:global(.dark) .markdown-renderer :global(a:hover) {
		color: #93c5fd;
	}

	:global(.dark) .markdown-renderer :global(em) {
		color: #d1d5db;
	}

	:global(.dark) .markdown-renderer :global(strong) {
		color: #f9fafb;
	}

	:global(.dark) .markdown-renderer :global(hr) {
		border-top-color: #4b5563;
	}

	/* Size variations */
	.markdown-renderer.text-sm :global(h1) {
		font-size: 1.25rem;
	}

	.markdown-renderer.text-sm :global(h2) {
		font-size: 1.125rem;
	}

	.markdown-renderer.text-sm :global(h3) {
		font-size: 1rem;
	}

	.markdown-renderer.text-sm :global(p) {
		font-size: 0.875rem;
	}

	.markdown-renderer.text-lg :global(h1) {
		font-size: 1.875rem;
	}

	.markdown-renderer.text-lg :global(h2) {
		font-size: 1.5rem;
	}

	.markdown-renderer.text-lg :global(h3) {
		font-size: 1.25rem;
	}

	.markdown-renderer.text-lg :global(p) {
		font-size: 1.1rem;
	}

	/* High contrast mode support */
	@media (prefers-contrast: high) {
		.markdown-renderer :global(a) {
			text-decoration: underline;
		}

		.markdown-renderer :global(code) {
			border: 1px solid #9ca3af;
		}

		:global(.dark) .markdown-renderer :global(code) {
			border: 1px solid #6b7280;
		}
	}

	/* Reduced motion support */
	@media (prefers-reduced-motion: reduce) {
		.markdown-renderer :global(a) {
			transition: none;
		}
	}

	/* User message theme - designed for blue background with white text */
	.theme-user.markdown-renderer :global(h1),
	.theme-user.markdown-renderer :global(h2),
	.theme-user.markdown-renderer :global(h3),
	.theme-user.markdown-renderer :global(h4),
	.theme-user.markdown-renderer :global(h5),
	.theme-user.markdown-renderer :global(h6) {
		color: #ffffff;
		border-bottom-color: rgba(255, 255, 255, 0.3);
	}

	.theme-user.markdown-renderer :global(p) {
		color: #f8fafc;
	}

	.theme-user.markdown-renderer :global(li) {
		color: #f8fafc;
	}

	.theme-user.markdown-renderer :global(blockquote) {
		background-color: rgba(255, 255, 255, 0.1);
		border-left-color: rgba(255, 255, 255, 0.4);
	}

	.theme-user.markdown-renderer :global(blockquote p) {
		color: #e2e8f0;
	}

	.theme-user.markdown-renderer :global(code) {
		background-color: rgba(255, 255, 255, 0.2);
		color: #ffffff;
	}

	.theme-user.markdown-renderer :global(pre) {
		background-color: rgba(0, 0, 0, 0.3);
		border-color: rgba(255, 255, 255, 0.2);
	}

	.theme-user.markdown-renderer :global(table) {
		border-color: rgba(255, 255, 255, 0.3);
	}

	.theme-user.markdown-renderer :global(thead) {
		background-color: rgba(255, 255, 255, 0.1);
	}

	.theme-user.markdown-renderer :global(th) {
		color: #ffffff;
		border-bottom-color: rgba(255, 255, 255, 0.3);
	}

	.theme-user.markdown-renderer :global(td) {
		color: #f8fafc;
		border-bottom-color: rgba(255, 255, 255, 0.2);
	}

	.theme-user.markdown-renderer :global(tbody tr:nth-child(even)) {
		background-color: rgba(255, 255, 255, 0.05);
	}

	.theme-user.markdown-renderer :global(a) {
		color: #bfdbfe;
	}

	.theme-user.markdown-renderer :global(a:hover) {
		color: #ffffff;
	}

	.theme-user.markdown-renderer :global(em) {
		color: #e2e8f0;
	}

	.theme-user.markdown-renderer :global(strong) {
		color: #ffffff;
	}

	.theme-user.markdown-renderer :global(hr) {
		border-top-color: rgba(255, 255, 255, 0.3);
	}
</style>
