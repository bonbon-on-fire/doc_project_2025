// src/lib/config.ts
// Centralized configuration for API endpoints
// This provides a single source of truth for API URLs throughout the client
//
// Port Configuration Strategy:
// 1. VITE_API_URL from AppHost (preferred) -> injected by Aspire via unified config
// 2. PUBLIC_API_BASE_URL from build-time env -> fallback for production
// 3. Environment-aware fallback -> matches appsettings.json ServicePorts
// 4. Smart detection of AppHost vs standalone development

import { PUBLIC_API_BASE_URL } from '$env/static/public';

/**
 * Unified port configuration matching server/appsettings.json
 * Used as fallback when VITE_API_URL injection fails
 */
const UNIFIED_PORTS = {
	development: 5130,
	test: 5099,
	production: 80
};

/**
 * Get the API base URL with intelligent fallback strategy
 *
 * Priority order:
 * 1. VITE_API_URL (injected by AppHost from unified config)
 * 2. PUBLIC_API_BASE_URL (production build-time)
 * 3. Environment-aware fallback (dev/test)
 */
export const getApiUrl = (): string => {
	// First priority: AppHost injected URL from unified configuration
	// This is the most reliable source - explicitly injected by Aspire
	const injectedUrl = import.meta.env.VITE_API_URL;
	if (injectedUrl) {
		console.debug('✅ Using VITE_API_URL (AppHost injected):', injectedUrl);
		return injectedUrl;
	}

	// Second priority: PUBLIC_API_BASE_URL for production builds
	// SvelteKit exposes VITE_* env vars as PUBLIC_* at build time
	if (PUBLIC_API_BASE_URL) {
		console.debug('✅ Using PUBLIC_API_BASE_URL (build-time):', PUBLIC_API_BASE_URL);
		return PUBLIC_API_BASE_URL;
	}

	// Third priority: Environment-aware intelligent fallback
	// Detect if we're in AppHost or standalone development
	const isAppHost =
		import.meta.env.VITE_ASPIRE_ENVIRONMENT === 'Development' ||
		(typeof window !== 'undefined' &&
			(window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') &&
			window.location.port === '5173');

	const environment = isAppHost ? 'development' : 'test';
	const port = UNIFIED_PORTS[environment as keyof typeof UNIFIED_PORTS];
	const fallbackUrl = `http://localhost:${port}`;

	console.warn(`⚠️ Falling back to environment-aware URL (AppHost=${isAppHost}): ${fallbackUrl}`);
	return fallbackUrl;
};

/**
 * Get WebSocket URL from API URL
 * Converts http:// to ws:// and https:// to wss://
 */
export const getWebSocketUrl = (): string => {
	const apiUrl = getApiUrl();
	return apiUrl.replace('http://', 'ws://').replace('https://', 'wss://');
};

/**
 * Centralized configuration object
 * Use this throughout the client for consistent API access
 */
export const config = {
	/** Base API URL */
	apiUrl: getApiUrl(),

	/** WebSocket base URL */
	wsUrl: getWebSocketUrl(),

	/** SignalR hub endpoint */
	signalRHub: `${getApiUrl()}/api/chat-hub`,

	/** SSE streaming endpoint */
	sseEndpoint: `${getApiUrl()}/api/chat/stream-sse`
};

export default config;
