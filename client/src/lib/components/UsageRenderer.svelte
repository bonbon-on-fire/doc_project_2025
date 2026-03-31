<script lang="ts">
	import type { RichMessageDto } from '$lib/types';

	// Component props
	export let message: RichMessageDto;
	export let isLatest: boolean = false;
	export let expanded: boolean = false;
	export let renderPhase: 'initial' | 'streaming' | 'complete' = 'initial';
	export let isLastAssistantMessage: boolean = false;

	// Usage messages are not displayed in the message list
	// The usage information is shown elsewhere in the UI (e.g., in a stats panel)
	// This renderer exists to prevent the fallback renderer from displaying raw usage data

	// Log for debugging purposes
	$: if (message.messageType === 'usage') {
		console.debug('[UsageRenderer] Suppressing usage message display:', {
			messageId: message.id,
			usage: (message as any).usage
		});
	}
</script>

<!-- Usage messages are intentionally not rendered in the message list -->
<!-- The component returns empty content to prevent fallback rendering -->
