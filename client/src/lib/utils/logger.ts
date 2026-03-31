import pino from 'pino';
import { browser, dev } from '$app/environment';
import { PUBLIC_API_BASE_URL } from '$env/static/public';

// Create different loggers for different environments
const createLogger = () => {
	if (browser) {
		// In browser, we can't write to files directly, so we'll send logs to the server
		return pino({
			level: dev ? 'trace' : 'info',
			browser: {
				asObject: true,
				write: {
					// Custom write function that sends logs to server
					info: (obj: any) => {
						sendLogToServer('info', obj);
					},
					error: (obj: any) => {
						sendLogToServer('error', obj);
					},
					debug: (obj: any) => {
						if (dev) sendLogToServer('debug', obj);
					},
					warn: (obj: any) => {
						sendLogToServer('warn', obj);
					},
					trace: (obj: any) => {
						if (dev) sendLogToServer('trace', obj);
					},
					fatal: (obj: any) => {
						sendLogToServer('fatal', obj);
					}
				}
			}
		});
	} else {
		// Server-side rendering context - use file transport
		return pino({
			level: dev ? 'trace' : 'info',
			transport: {
				target: 'pino/file',
				options: {
					destination: '../../logs/client/app.jsonl',
					mkdir: true
				}
			}
		});
	}
};

// Resolve API base URL similar to ApiClient
const RESOLVED_API_BASE = (() => {
	const envBase = PUBLIC_API_BASE_URL as any as string | undefined;
	if (envBase && typeof envBase === 'string' && envBase.length > 0) return envBase;
	if (typeof window !== 'undefined' && window.location.port === '5173') {
		return 'http://localhost:5099';
	}
	return '';
})();

// Function to send logs to server endpoint
async function sendLogToServer(level: string, logObject: any) {
	try {
		// Add timestamp and format for JSONL
		const logEntry = {
			timestamp: new Date().toISOString(),
			level,
			...logObject,
			source: 'client'
		};

		// Send to server endpoint
		const url = RESOLVED_API_BASE ? `${RESOLVED_API_BASE}/api/logs` : '/api/logs';
		await fetch(url, {
			method: 'POST',
			headers: {
				'Content-Type': 'application/json'
			},
			body: JSON.stringify(logEntry)
		}).catch(() => {
			// Silently fail if server is not available
			// Fall back to console logging in development
			if (dev) {
				console.log(`[${level.toUpperCase()}]`, logObject);
			}
		});
	} catch (error) {
		// Silently fail logging errors to avoid infinite loops
		if (dev) {
			console.error('Failed to send log to server:', error);
		}
	}
}

export const logger = createLogger();
