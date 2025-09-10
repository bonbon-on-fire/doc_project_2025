/**
 * Server Capabilities Service
 * Detects and caches server capabilities including Orleans availability
 */

import { browser } from '$app/environment';

export interface ServerCapabilities {
	version: string;
	environment: string;
	timestamp: string;
	orleans: OrleansCapabilities;
	features: Record<string, boolean>;
	streamingProtocols: string[];
}

export interface OrleansCapabilities {
	enabled: boolean;
	available: boolean;
	coHosted: boolean;
	healthy: boolean;
	routingEnabled: boolean;
	clusterSize: number;
}

class CapabilitiesService {
	private capabilities: ServerCapabilities | null = null;
	private lastCheck: number = 0;
	private readonly CACHE_DURATION = 60000; // 1 minute cache
	private checking = false;

	/**
	 * Get server capabilities, fetching if needed
	 */
	async getCapabilities(forceRefresh = false): Promise<ServerCapabilities> {
		// Return cached capabilities if still valid
		if (!forceRefresh && this.capabilities && Date.now() - this.lastCheck < this.CACHE_DURATION) {
			return this.capabilities;
		}

		// Avoid multiple simultaneous checks
		if (this.checking) {
			// Wait for current check to complete
			await new Promise((resolve) => setTimeout(resolve, 100));
			return this.capabilities || this.getDefaultCapabilities();
		}

		this.checking = true;
		try {
			const response = await fetch('/api/capabilities', {
				headers: {
					Accept: 'application/json'
				}
			});

			if (response.ok) {
				this.capabilities = await response.json();
				this.lastCheck = Date.now();

				// Store in session storage for quick access
				if (browser) {
					sessionStorage.setItem('server-capabilities', JSON.stringify(this.capabilities));
					sessionStorage.setItem('capabilities-timestamp', this.lastCheck.toString());
				}

				console.log('Server capabilities updated:', this.capabilities);
			} else {
				console.warn('Failed to fetch server capabilities:', response.status);
				this.capabilities = this.getDefaultCapabilities();
			}
		} catch (error) {
			console.error('Error fetching server capabilities:', error);

			// Try to load from session storage
			if (browser) {
				const stored = sessionStorage.getItem('server-capabilities');
				if (stored) {
					try {
						this.capabilities = JSON.parse(stored);
						console.log('Using cached server capabilities');
					} catch {
						this.capabilities = this.getDefaultCapabilities();
					}
				} else {
					this.capabilities = this.getDefaultCapabilities();
				}
			} else {
				this.capabilities = this.getDefaultCapabilities();
			}
		} finally {
			this.checking = false;
		}

		return this.capabilities!;
	}

	/**
	 * Check if Orleans is available and should be used
	 */
	async isOrleansAvailable(): Promise<boolean> {
		const caps = await this.getCapabilities();
		return caps.orleans.enabled && caps.orleans.available && caps.orleans.routingEnabled;
	}

	/**
	 * Check if a specific feature is enabled
	 */
	async isFeatureEnabled(feature: string): Promise<boolean> {
		const caps = await this.getCapabilities();
		return caps.features[feature] ?? false;
	}

	/**
	 * Check if a streaming protocol is supported
	 */
	async supportsProtocol(protocol: string): Promise<boolean> {
		const caps = await this.getCapabilities();
		return caps.streamingProtocols.includes(protocol);
	}

	/**
	 * Get Orleans status string for UI display
	 */
	async getOrleansStatus(): Promise<string> {
		const caps = await this.getCapabilities();
		const orleans = caps.orleans;

		if (!orleans.enabled) {
			return 'Disabled';
		}

		if (!orleans.available) {
			return 'Not Available';
		}

		if (!orleans.healthy) {
			return 'Unhealthy';
		}

		if (orleans.coHosted) {
			return `Co-hosted (${orleans.clusterSize} silo${orleans.clusterSize !== 1 ? 's' : ''})`;
		}

		return `Connected (${orleans.clusterSize} silo${orleans.clusterSize !== 1 ? 's' : ''})`;
	}

	/**
	 * Clear cached capabilities
	 */
	clearCache(): void {
		this.capabilities = null;
		this.lastCheck = 0;

		if (browser) {
			sessionStorage.removeItem('server-capabilities');
			sessionStorage.removeItem('capabilities-timestamp');
		}
	}

	/**
	 * Get default capabilities when server is unreachable
	 */
	private getDefaultCapabilities(): ServerCapabilities {
		return {
			version: '1.0.0',
			environment: 'Unknown',
			timestamp: new Date().toISOString(),
			orleans: {
				enabled: false,
				available: false,
				coHosted: false,
				healthy: false,
				routingEnabled: false,
				clusterSize: 0
			},
			features: {},
			streamingProtocols: ['SSE'] // SSE is always available
		};
	}
}

// Export singleton instance
export const capabilitiesService = new CapabilitiesService();

// Auto-refresh capabilities on page load/refresh
if (browser) {
	// Check capabilities on load
	capabilitiesService.getCapabilities().catch(console.error);

	// Refresh when page becomes visible again
	document.addEventListener('visibilitychange', () => {
		if (!document.hidden) {
			capabilitiesService.getCapabilities().catch(console.error);
		}
	});
}
