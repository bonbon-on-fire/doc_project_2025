// API client for communicating with ASP.NET backend
import { PUBLIC_API_BASE_URL } from '$env/static/public';
import type { ChatDto, CreateChatRequest, ChatHistoryResponse } from '$lib/types/chat';

export class ApiClient {
	private baseUrl: string;

	constructor(baseUrl: string = (PUBLIC_API_BASE_URL as any) || '') {
		// Prefer explicit env, otherwise in dev (vite on 5173) hit server directly to avoid proxy buffering SSE
		if (!baseUrl && typeof window !== 'undefined' && window.location.port === '5173') {
			baseUrl = 'http://localhost:5099';
		}
		this.baseUrl = baseUrl || '';
	}

	private async request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
		const url = this.baseUrl ? `${this.baseUrl}${endpoint}` : endpoint;

		const config: RequestInit = {
			headers: {
				'Content-Type': 'application/json',
				...options.headers
			},
			...options
		};

		try {
			const response = await fetch(url, config);

			if (!response.ok) {
				const errorText = await response.text();
				throw new Error(`HTTP ${response.status}: ${errorText}`);
			}

			return await response.json();
		} catch (error) {
			console.error(`API request failed: ${endpoint}`, error);
			throw error;
		}
	}

	// Chat endpoints
	async getChatHistory(
		userId: string,
		page: number = 1,
		pageSize: number = 20
	): Promise<ChatHistoryResponse> {
		return this.request<ChatHistoryResponse>(
			`/api/chat/history?userId=${userId}&page=${page}&pageSize=${pageSize}`
		);
	}

	async getChat(chatId: string): Promise<ChatDto> {
		return this.request<ChatDto>(`/api/chat/${chatId}`);
	}

	async createChat(request: CreateChatRequest): Promise<ChatDto> {
		return this.request<ChatDto>('/api/chat', {
			method: 'POST',
			body: JSON.stringify(request)
		});
	}

	async deleteChat(chatId: string): Promise<void> {
		await this.request(`/api/chat/${chatId}`, {
			method: 'DELETE'
		});
	}

	async streamChatCompletion(request: CreateChatRequest): Promise<Response> {
		const url = this.baseUrl ? `${this.baseUrl}/api/chat/stream-sse` : `/api/chat/stream-sse`;

		const response = await fetch(url, {
			method: 'POST',
			headers: {
				'Content-Type': 'application/json'
			},
			body: JSON.stringify(request)
		});

		if (!response.ok) {
			const errorText = await response.text();
			throw new Error(`HTTP ${response.status}: ${errorText}`);
		}

		return response;
	}

	// Health check
	async getHealth(): Promise<{ status: string; timestamp: string }> {
		return this.request('/api/health');
	}
}

// Export singleton instance
export const apiClient = new ApiClient();
