# Chat Modes Implementation Tasks

## Overview
This document contains the implementation tasks for the Chat Modes feature based on the design document. Tasks are organized by phase and include specific acceptance criteria, dependencies, and testing requirements.

## Task Breakdown

### Phase 1: Core Infrastructure (Priority: Critical)

#### Task 1.1: Create Mode Type Definitions
- [x] Create `shared/types/mode.ts` with Mode interface
  - [x] Define all required fields (id, name, description, prompt, tools, etc.)
  - [x] Export type for use in both client and server
  - [x] Add JSDoc comments for all fields
- Requirements:
  - [x] Design document section: Data Models
- Tests:
  - [x] TypeScript compiles without errors
  - [x] Types are importable in both client and server code

#### Task 1.2: Add Database Schema for User Modes
- [x] Update `server/Storage/Sqlite/SchemaHelper.cs` to create user_modes table
  - [x] Add table creation SQL to EnsureSchemaAsync method
  - [x] Add index on UserId column
  - [x] Update schema version
- [x] Create `server/Storage/IModeStorage.cs` interface
  - [x] Define CRUD operations for user modes
- [x] Implement `server/Storage/Sqlite/SqliteModeStorage.cs`
  - [x] Implement all IModeStorage methods
  - [x] Add proper error handling
  - [x] Use existing SqliteConnectionFactory pattern
- Requirements:
  - [x] Database Schema Addition from design doc
- Tests:
  - [x] Schema creates successfully on fresh database
  - [x] Schema updates correctly on existing database
  - [x] CRUD operations work correctly

#### Task 1.3: Create System Modes Directory Structure
- [x] Create `server/modes/` directory
  - [x] Add to .gitignore for user-specific test modes
  - [x] Include in deployment package
- [x] Create default system mode JSON files:
  - [x] `general.json` - Default mode with all tools
  - [x] `coding.json` - Programming-focused mode
  - [x] `writing.json` - Content creation mode
  - [x] `research.json` - Research and analysis mode
- Requirements:
  - [x] System Mode File Format from design doc
- Tests:
  - [x] JSON files are valid and parseable
  - [x] All required fields are present

### Phase 2: Backend Service Implementation (Priority: Critical)

#### Task 2.1: Implement ModeService
- [x] Create `server/Services/IModeService.cs` interface
  - [x] Define all service methods from design doc
- [x] Implement `server/Services/ModeService.cs`
  - [x] Load system modes from JSON files on startup
  - [x] Implement GetAllModesAsync (combine system + user modes)
  - [x] Implement GetModeByIdAsync
  - [x] Implement CreateCustomModeAsync
  - [x] Implement UpdateCustomModeAsync
  - [x] Implement DeleteCustomModeAsync
  - [x] Implement FilterToolsByMode method
  - [x] Add in-memory caching for system modes
  - [x] Add hot-reload for development mode
- [x] Register service in Program.cs
  - [x] Add as Singleton for system mode caching
- Requirements:
  - [x] ModeService specification from design doc
- Tests:
  - [x] Unit tests for all service methods
  - [x] System modes load correctly
  - [x] Tool filtering works as expected
  - [x] Cache invalidation works properly

#### Task 2.2: Modify ChatService for Mode Support
- [x] Update `ChatService.cs`
  - [x] Add IModeService dependency injection
  - [x] Modify CreateChatSpecificFunctionCallMiddleware to accept modeId parameter
  - [x] Implement tool filtering based on mode
  - [x] Apply mode's system prompt if specified
  - [x] Handle mode's defaultModel preference
- [x] Update CreateChatAsync method
  - [x] Accept modeId parameter
  - [x] Apply mode configuration
- [x] Update ContinueChatAsync method
  - [x] Accept modeId for mode switching
  - [x] Handle mode transition logic
- Requirements:
  - [x] ChatService Modifications from design doc
- Tests:
  - [x] Chat creation with mode works
  - [x] Tool filtering applies correctly
  - [x] Mode switching mid-conversation works
  - [x] Fallback to general mode works

#### Task 2.3: Add Mode API Endpoints
- [x] Create `server/Controllers/ModeController.cs`
  - [x] GET /api/modes - List all modes
  - [x] GET /api/modes/{id} - Get specific mode
  - [x] POST /api/modes - Create custom mode
  - [x] PUT /api/modes/{id} - Update custom mode
  - [x] DELETE /api/modes/{id} - Delete custom mode
  - [x] Add proper authorization checks
  - [x] Add validation for mode data
- [x] Update `ChatController.cs`
  - [x] Add modeId to CreateChatRequest DTO
  - [x] Add modeId to ContinueChatRequest DTO
  - [x] Pass modeId to ChatService methods
- Requirements:
  - [x] API Endpoints from design doc
- Tests:
  - [x] API endpoints return correct data
  - [x] Authorization works correctly
  - [x] Validation rejects invalid data
  - [x] Integration with ChatService works

### Phase 3: Frontend Implementation (Priority: High)

#### Task 3.1: Create ModeSelector Component
- [x] Create `client/src/lib/components/ModeSelector.svelte`
  - [x] Implement dropdown UI with accessibility features
  - [x] Fetch modes from API on mount with auto-initialization
  - [x] Handle mode selection events with proper event dispatching
  - [x] Show mode description in tooltip with tool information
  - [x] Group modes by category (Task, Role, Custom, System)
  - [x] Handle loading and error states with retry functionality
- [x] Add proper TypeScript types
- [x] Style with existing Tailwind classes following design system
- [x] Extended API client with mode operations
- [x] Created comprehensive modes store with loose coupling
- Requirements:
  - [x] ModeSelector Component from design doc
- Tests:
  - [x] Component compiles without TypeScript errors
  - [x] Mode selection events are properly dispatched
  - [x] Loading state displays properly with spinner
  - [x] Error handling works with retry functionality

#### Task 3.2: Integrate Mode Selection into Chat UI
- [x] Modify `client/src/lib/components/ChatWindow.svelte`
  - [x] Import and add ModeSelector component to welcome screen and chat header
  - [x] Position selector in welcome screen and compact version in header
  - [x] Track selectedMode state through modes store
  - [x] Pass modeId in chat creation API requests
- [x] Update chat API service
  - [x] Add modeId to CreateChatRequest type
  - [x] Add mode information to ChatDto type
  - [x] Include modeId in API calls for chat creation
- [x] Update chat stores and orchestrator
  - [x] Store current mode selection in modes store
  - [x] Updated chat actions to accept modeId parameter
  - [x] Enhanced HandlerBasedSSEOrchestrator for mode support
- [x] Add mode information display
  - [x] Show current mode in chat header
  - [x] Display mode information for active chats
- Requirements:
  - [x] ChatWindow Integration from design doc
- Tests:
  - [x] Mode selector appears in UI (welcome screen and header)
  - [x] Mode selection persists through modes store
  - [x] API requests include modeId for new chats
  - [x] Build completes successfully without errors

#### Task 3.3: Add Mode Information Display
- [x] Create mode indicator in chat header
  - [x] Show current mode name with visual indicator
  - [x] Display available tools count in ModeSelector tooltip
  - [x] Add mode description tooltip in ModeSelector
- [x] Add visual feedback for mode changes
  - [x] Subtle animation on mode switch (scale, bounce, background flash)
  - [x] Show toast notification system for mode changes
  - [x] Created comprehensive Toast component with multiple types
  - [x] Integrated toast notifications into main layout
- [x] Handle unavailable tools gracefully
  - [x] Show warning badges if tools are missing
  - [x] List unavailable tools in tooltips and dropdowns
  - [x] Color-coded tool availability (green checkmarks, yellow warnings)
  - [x] Detailed tool availability tracking in stores
  - [x] Toast notifications for tool availability warnings
- Requirements:
  - [x] Requirement 2 (Mode information display) - Complete
  - [x] Requirement 8 (Visual feedback) - Complete
- Tests:
  - [x] Mode information displays correctly in header and selector
  - [x] Visual feedback works (animations, toasts)
  - [x] Warnings appear for missing tools with detailed information
  - [x] Build completes successfully with all new features

### Phase 4: Testing & Polish (Priority: High)

#### Task 4.1: Unit Tests
- [ ] Write unit tests for ModeService
  - [ ] Test all CRUD operations
  - [ ] Test tool filtering logic
  - [ ] Test caching behavior
- [ ] Write unit tests for mode storage
  - [ ] Test database operations
  - [ ] Test error handling
- [ ] Write unit tests for API controllers
  - [ ] Test all endpoints
  - [ ] Test validation
- Requirements:
  - [ ] Testing Strategy from design doc
- Tests:
  - [ ] 80%+ code coverage
  - [ ] All edge cases covered
  - [ ] Tests run in CI/CD

#### Task 4.2: Integration Tests
- [ ] Test mode selection flow
  - [ ] Select mode → Send message → Verify tools
  - [ ] Switch modes mid-conversation
  - [ ] Create and use custom mode
- [ ] Test error scenarios
  - [ ] Missing tools handling
  - [ ] Invalid mode data
  - [ ] Network failures
- [ ] Test performance
  - [ ] Mode loading time
  - [ ] Switching latency
- Requirements:
  - [ ] All acceptance criteria from requirements.md
- Tests:
  - [ ] End-to-end flows work
  - [ ] Error handling is graceful
  - [ ] Performance meets targets

#### Task 4.3: Documentation and Cleanup
- [ ] Add user documentation
  - [ ] How to use modes
  - [ ] Creating custom modes
  - [ ] Available system modes
- [ ] Update API documentation
  - [ ] Document new endpoints
  - [ ] Update existing endpoint docs
- [ ] Code cleanup
  - [ ] Remove debug code
  - [ ] Add missing comments
  - [ ] Refactor if needed
- Requirements:
  - [ ] Clean, maintainable code
- Tests:
  - [ ] Documentation is accurate
  - [ ] Code passes linting
  - [ ] No console errors

### Phase 5: Custom Mode Editor (Priority: Medium)

#### Task 5.1: Create Mode Editor UI
- [ ] Create `client/src/lib/components/ModeEditor.svelte`
  - [ ] Form for mode properties
  - [ ] Multi-select for tools
  - [ ] Prompt text editor
  - [ ] Model selection dropdown
- [ ] Add validation
  - [ ] Required fields
  - [ ] Prompt length limits
  - [ ] Valid tool selection
- [ ] Add save/cancel actions
- Requirements:
  - [ ] Requirement 4 from requirements.md
- Tests:
  - [ ] Form renders correctly
  - [ ] Validation works
  - [ ] Save creates/updates mode

#### Task 5.2: Mode Management Page
- [ ] Create `/modes` route
  - [ ] List user's custom modes
  - [ ] Edit/Delete actions
  - [ ] Create new mode button
- [ ] Add mode preview
  - [ ] Show mode details
  - [ ] Test mode option
- [ ] Add mode import/export
  - [ ] Export as JSON
  - [ ] Import from JSON
- Requirements:
  - [ ] Custom mode management
- Tests:
  - [ ] CRUD operations work
  - [ ] Import/export works
  - [ ] UI is responsive

## Dependencies

### External Dependencies
- No new npm packages required
- No new NuGet packages required
- Uses existing infrastructure

### Internal Dependencies
- Requires existing auth system for user context
- Depends on MCP client manager for tool list
- Uses existing chat infrastructure

## Definition of Done

Each task is considered complete when:
1. Code is implemented according to specifications
2. Unit tests are written and passing
3. Integration tests are passing
4. Code review is complete
5. Documentation is updated
6. No regression in existing features

## Risk Mitigation

### Identified Risks
1. **Tool availability changes**: Mitigated by graceful degradation
2. **Performance impact**: Mitigated by caching and lazy loading
3. **Migration issues**: Mitigated by backward compatibility
4. **User confusion**: Mitigated by clear UI and documentation

## Rollout Strategy

1. **Phase 1-2**: Deploy backend with feature flag disabled
2. **Phase 3**: Enable for internal testing
3. **Phase 4**: Beta release to subset of users
4. **Phase 5**: Full release with custom modes
5. **Monitor**: Track usage and gather feedback

## Success Metrics

- Mode adoption rate > 30% of active users
- Custom mode creation > 10% of active users
- No increase in error rates
- Chat latency remains < 200ms
- User satisfaction score maintained or improved