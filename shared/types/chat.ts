// Shared type definitions for chat functionality

export interface Message {
  id: string;
  chatId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: Date;
  sequenceNumber: number;
  isHidden?: boolean;
}

export interface Chat {
  id: string;
  userId: string;
  title: string;
  messages: Message[];
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateChatRequest {
  message: string;
  systemPrompt?: string;
}

export interface ContinueChatRequest {
  chatId: string;
  message: string;
}

export interface ChatResponse {
  chatId: string;
  messageId: string;
  content: string;
  role: 'assistant';
  timestamp: Date;
}

export interface StreamChatResponse {
  chatId: string;
  messageId: string;
  delta: string;
  role: 'assistant';
  done: boolean;
}

export interface ChatHistoryResponse {
  chats: Chat[];
  totalCount: number;
  page: number;
  pageSize: number;
}