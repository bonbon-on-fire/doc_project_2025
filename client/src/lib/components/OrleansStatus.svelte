<script lang="ts">
	import { onMount } from 'svelte';
	import { capabilitiesService } from '$lib/services/capabilities';

	let orleansStatus = 'Checking...';
	let statusClass = 'text-gray-500';
	let showDetails = false;
	let capabilities: any = null;

	async function updateStatus() {
		try {
			capabilities = await capabilitiesService.getCapabilities();
			orleansStatus = await capabilitiesService.getOrleansStatus();

			// Set status color based on Orleans state
			if (capabilities.orleans.routingEnabled) {
				statusClass = 'text-green-500';
			} else if (capabilities.orleans.available) {
				statusClass = 'text-yellow-500';
			} else {
				statusClass = 'text-gray-500';
			}
		} catch (error) {
			console.error('Failed to get Orleans status:', error);
			orleansStatus = 'Unknown';
			statusClass = 'text-red-500';
		}
	}

	onMount(() => {
		updateStatus();

		// Refresh status periodically
		const interval = setInterval(updateStatus, 30000); // Every 30 seconds

		return () => clearInterval(interval);
	});
</script>

<div class="orleans-status">
	<button
		type="button"
		class="flex items-center gap-2 text-xs {statusClass} hover:opacity-80"
		on:click={() => (showDetails = !showDetails)}
		title="Click for details"
	>
		<svg class="h-3 w-3" fill="currentColor" viewBox="0 0 20 20">
			<circle cx="10" cy="10" r="3" />
		</svg>
		<span>Orleans: {orleansStatus}</span>
	</button>

	{#if showDetails && capabilities}
		<div
			class="absolute z-50 mt-2 rounded-lg border border-gray-200 bg-white p-4 shadow-lg dark:border-gray-700 dark:bg-gray-800"
		>
			<h3 class="mb-2 font-semibold">Server Capabilities</h3>

			<div class="space-y-2 text-sm">
				<div>
					<span class="text-gray-500">Environment:</span>
					<span class="ml-2">{capabilities.environment}</span>
				</div>

				<div>
					<span class="text-gray-500">Version:</span>
					<span class="ml-2">{capabilities.version}</span>
				</div>

				<h4 class="mb-1 mt-3 font-semibold">Orleans</h4>
				<div class="space-y-1 pl-2">
					<div>
						<span class="text-gray-500">Enabled:</span>
						<span class="ml-2">{capabilities.orleans.enabled ? '✅' : '❌'}</span>
					</div>
					<div>
						<span class="text-gray-500">Available:</span>
						<span class="ml-2">{capabilities.orleans.available ? '✅' : '❌'}</span>
					</div>
					<div>
						<span class="text-gray-500">Healthy:</span>
						<span class="ml-2">{capabilities.orleans.healthy ? '✅' : '❌'}</span>
					</div>
					<div>
						<span class="text-gray-500">Routing:</span>
						<span class="ml-2">{capabilities.orleans.routingEnabled ? '✅' : '❌'}</span>
					</div>
					{#if capabilities.orleans.clusterSize > 0}
						<div>
							<span class="text-gray-500">Cluster Size:</span>
							<span class="ml-2">{capabilities.orleans.clusterSize} silo(s)</span>
						</div>
					{/if}
				</div>

				<h4 class="mb-1 mt-3 font-semibold">Features</h4>
				<div class="space-y-1 pl-2">
					{#each Object.entries(capabilities.features) as [feature, enabled]}
						<div>
							<span class="text-gray-500">{feature}:</span>
							<span class="ml-2">{enabled ? '✅' : '❌'}</span>
						</div>
					{/each}
				</div>

				<h4 class="mb-1 mt-3 font-semibold">Protocols</h4>
				<div class="pl-2">
					{capabilities.streamingProtocols.join(', ')}
				</div>
			</div>

			<button
				type="button"
				class="mt-3 text-xs text-blue-500 hover:underline"
				on:click={() => {
					capabilitiesService.clearCache();
					updateStatus();
				}}
			>
				Refresh
			</button>
		</div>
	{/if}
</div>

<style>
	.orleans-status {
		position: relative;
	}
</style>
