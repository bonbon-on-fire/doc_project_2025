import devtoolsJson from 'vite-plugin-devtools-json';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [sveltekit(), devtoolsJson()],
	test: {
		expect: { requireAssertions: true },
		projects: [
			{
				extends: './vite.config.ts',
				test: {
					name: 'client',
					environment: 'browser',
					browser: {
						enabled: true,
						provider: 'playwright',
						instances: [{ browser: 'chromium' }]
					},
					include: ['src/**/*.svelte.{test,spec}.{js,ts}'],
					exclude: ['src/lib/server/**'],
					setupFiles: ['./vitest-setup-client.ts']
				}
			},
			{
				extends: './vite.config.ts',
				test: {
					name: 'server',
					environment: 'node',
					include: ['src/**/*.{test,spec}.{js,ts}'],
					exclude: ['src/**/*.svelte.{test,spec}.{js,ts}']
				}
			}
		]
	},
	server: {
		// Use Aspire-injected PORT or fallback to 5173
		port: parseInt(process.env.PORT || '5173'),
		proxy: {
			'/api': {
				// Use Aspire-injected VITE_API_URL or fallback to localhost:5099
				// Aspire injects this via AppHost environment variable configuration
				target: process.env.VITE_API_URL || 'http://localhost:5099',
				changeOrigin: true,
				secure: false
			},
			// WebSocket proxy for SignalR hub
			'/api/chat-hub': {
				target: process.env.VITE_API_URL || 'http://localhost:5099',
				changeOrigin: true,
				ws: true // Enable WebSocket support
			}
		}
	}
});
