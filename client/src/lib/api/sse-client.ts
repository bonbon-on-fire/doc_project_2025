import { PUBLIC_API_BASE_URL } from '$env/static/public';
import type { CreateChatRequest } from '$lib/types/chat';

export async function streamChat(
	payload: CreateChatRequest,
	onChunk: (chunk: any) => void,
	onComplete: (data: any) => void,
	onError: (error: any) => void
) {
	try {
		let base = PUBLIC_API_BASE_URL as any;
		// Prefer explicit env; if not set and running vite on 5173 (dev/test), default to Test server 5099
		if (!base && typeof window !== 'undefined' && window.location.port === '5173') {
			base = 'http://localhost:5099';
		}
		const response = await fetch(`${base || ''}/api/chat/stream-sse`, {
			method: 'POST',
			headers: {
				'Content-Type': 'application/json'
			},
			body: JSON.stringify(payload)
		});

		if (!response.body) return;

		const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
		let buffer = '';

		while (true) {
			const { value, done } = await reader.read();
			if (done) break;

			buffer += value;
			let boundary = buffer.indexOf('\n\n');

			while (boundary !== -1) {
				const eventString = buffer.substring(0, boundary);
				buffer = buffer.substring(boundary + 2);

				if (eventString) {
					const eventTypeMatch = eventString.match(/^event: (.*)$/m);
					const dataMatch = eventString.match(/^data: (.*)$/m);

					if (eventTypeMatch && dataMatch) {
						const eventType = eventTypeMatch[1];
						const data = JSON.parse(dataMatch[1]);

						if (eventType === 'chunk') {
							onChunk(data);
						} else if (eventType === 'complete') {
							onComplete(data);
						}
					}
				}
				boundary = buffer.indexOf('\n\n');
			}
		}
	} catch (err) {
		onError(err);
	}
}
