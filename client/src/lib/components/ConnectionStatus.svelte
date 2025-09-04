<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import {
		connectionState,
		connectionStatusMessage,
		connectionStatusColor
	} from '$lib/stores/signalr';
	import type { ConnectionState } from '$lib/services/signalr';

	// Show/hide status based on connection state
	let showStatus = true;
	let statusState: ConnectionState = 'disconnected';
	let statusMessage = '';
	let statusColor = 'gray';

	// Subscribe to connection state
	const unsubscribeState = connectionState.subscribe((value) => {
		statusState = value;
		// Show status for non-connected states
		showStatus = value !== 'connected';

		// Auto-hide after 3 seconds when connected
		if (value === 'connected') {
			setTimeout(() => {
				if (statusState === 'connected') {
					showStatus = false;
				}
			}, 3000);
		}
	});

	const unsubscribeMessage = connectionStatusMessage.subscribe((value) => {
		statusMessage = value;
	});

	const unsubscribeColor = connectionStatusColor.subscribe((value) => {
		statusColor = value;
	});

	onDestroy(() => {
		unsubscribeState();
		unsubscribeMessage();
		unsubscribeColor();
	});

	// Determine icon based on state
	$: icon = getIcon(statusState);

	function getIcon(state: ConnectionState): string {
		switch (state) {
			case 'connected':
				return '✓';
			case 'connecting':
			case 'reconnecting':
				return '⟳';
			case 'disconnected':
			case 'disconnecting':
				return '○';
			case 'error':
				return '✗';
			default:
				return '?';
		}
	}

	// Determine CSS classes
	$: statusClasses = `connection-status connection-status--${statusColor}`;
	$: pulseClass = statusState === 'connecting' || statusState === 'reconnecting' ? 'pulse' : '';
</script>

{#if showStatus}
	<div class={statusClasses} class:pulse={pulseClass}>
		<span class="connection-status__icon">{icon}</span>
		<span class="connection-status__message">{statusMessage}</span>
	</div>
{/if}

<style>
	.connection-status {
		position: fixed;
		top: 1rem;
		right: 1rem;
		padding: 0.5rem 1rem;
		border-radius: 0.5rem;
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-size: 0.875rem;
		background-color: rgba(255, 255, 255, 0.95);
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
		z-index: 1000;
		transition: all 0.3s ease;
	}

	.connection-status--green {
		background-color: #d4edda;
		color: #155724;
		border: 1px solid #c3e6cb;
	}

	.connection-status--yellow {
		background-color: #fff3cd;
		color: #856404;
		border: 1px solid #ffeaa7;
	}

	.connection-status--gray {
		background-color: #f8f9fa;
		color: #6c757d;
		border: 1px solid #dee2e6;
	}

	.connection-status--red {
		background-color: #f8d7da;
		color: #721c24;
		border: 1px solid #f5c6cb;
	}

	.connection-status__icon {
		font-weight: bold;
		font-size: 1rem;
	}

	.connection-status__message {
		white-space: nowrap;
	}

	.pulse {
		animation: pulse 2s infinite;
	}

	@keyframes pulse {
		0% {
			opacity: 1;
		}
		50% {
			opacity: 0.6;
		}
		100% {
			opacity: 1;
		}
	}

	/* Dark mode support */
	:global(.dark) .connection-status {
		background-color: rgba(30, 30, 30, 0.95);
	}

	:global(.dark) .connection-status--green {
		background-color: #1e3a2e;
		color: #a3cfbb;
		border-color: #2d5a47;
	}

	:global(.dark) .connection-status--yellow {
		background-color: #3a2e1e;
		color: #cfbba3;
		border-color: #5a472d;
	}

	:global(.dark) .connection-status--gray {
		background-color: #2d2d2d;
		color: #a0a0a0;
		border-color: #4a4a4a;
	}

	:global(.dark) .connection-status--red {
		background-color: #3a1e1e;
		color: #cfa3a3;
		border-color: #5a2d2d;
	}
</style>
