// Task data structures matching the LmDotNet TaskManager tool

export type TaskStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'Removed';

export interface TaskItem {
  id: number;
  title: string;
  status: TaskStatus;
  parentId?: number;
  subtasks?: TaskItem[];
  notes?: string[];
}

export interface ChatTaskState {
  chatId: string;
  tasks: TaskItem[];
  version: number;
  lastUpdatedUtc: string;
}

export interface BulkTaskItem {
  task: string;
  subTasks?: string[];
  notes?: string[];
}

export interface TaskOperation {
  type: 'add-task' | 'bulk-initialize' | 'update-task' | 'delete-task' | 
        'manage-notes' | 'list-notes' | 'get-task' | 'list-tasks' | 'search-tasks';
  taskId?: number;
  subtaskId?: number;
  parentId?: number;
  title?: string;
  status?: TaskStatus | string; // Allow string for flexible status parsing
  tasks?: BulkTaskItem[]; // For bulk-initialize
  clearExisting?: boolean; // For bulk-initialize
  noteText?: string; // For manage-notes
  noteIndex?: number; // For manage-notes (1-based)
  action?: 'add' | 'edit' | 'delete'; // For manage-notes
  searchTerm?: string; // For search-tasks
  countType?: 'total' | 'completed' | 'pending' | 'removed'; // For search-tasks
  filterStatus?: TaskStatus | string; // For list-tasks
  mainOnly?: boolean; // For list-tasks
}

export interface TaskOperationEvent {
  eventType: 'task_operation_start' | 'task_operation_complete' | 'task_state_sync';
  chatId: string;
  messageId?: string;
  operation?: TaskOperation;
  taskState?: TaskItem[];
  timestamp: string;
}

// API response types
export interface GetTasksResponse {
  chatId: string;
  tasks: any; // JsonElement from server
  version: number;
}

export interface TaskErrorInfo {
  code: string;
  message: string;
  currentVersion?: number;
}