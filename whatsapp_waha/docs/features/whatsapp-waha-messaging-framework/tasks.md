# Task List: WhatsApp WAHA Messaging Framework

## Important Note

You MUST keep this file checklist up to date as you complete the tasks. This file is SHARED with team members and it helps them see the progress.

## Overview
Implementation tasks for the WhatsApp WAHA messaging framework, organized by priority and dependencies. Each task includes subtasks, requirements mapping, and test criteria.

---

## Task 1: Project Setup and Core Infrastructure [✅ 100% Complete]
- [x] **Create solution structure with .NET Core 9.0 projects**
  - [x] Create solution file `WhatsAppWahaFramework.sln`
  - [x] Create `WhatsAppWaha.Core` class library project
  - [x] Create `WhatsAppWaha.HelloWorld` console application project
  - [x] Create `WhatsAppWaha.MessageReceiver` console application project
  - [x] Create test projects for each component
  - [x] Configure project references and dependencies
  - **Requirements:**
    - [x] Requirement 6: .NET Core 9.0 Modern Architecture
  - **Tests:**
    - [x] Test 1: Verify all projects build successfully
    - [x] Test 2: Verify project references are correctly configured
    - [x] Test 3: Verify .NET Core 9.0 target framework

- [x] **Set up dependency injection and configuration infrastructure**
  - [x] Create `ServiceCollectionExtensions.cs` for DI registration
  - [x] Implement configuration models (`WahaSettings`, `NtfySettings`, `AppSettings`)
  - [x] Set up configuration binding with validation
  - [x] Configure structured logging with Serilog
  - [x] Implement global exception handling patterns
  - **Requirements:**
    - [x] Requirement 6: .NET Core 9.0 Modern Architecture
    - [x] Requirement 8: Configuration Management
  - **Tests:**
    - [x] Test 1: Verify DI container resolves all services
    - [x] Test 2: Verify configuration binding works correctly
    - [x] Test 3: Verify logging writes to configured sinks
    - [x] **Added 70 comprehensive unit tests covering all infrastructure**

---

## 🚨 CRITICAL ARCHITECTURAL FIXES NEEDED - WAHA SDK NOT BEING USED

### Current Problem
The implementation has the WAHA NuGet package installed (`Waha v1.1.0`) but is **completely bypassing it** and making raw HTTP calls instead. This is a fundamental architectural flaw.

**What's Wrong:**
```csharp
// ❌ CURRENT (BROKEN): Raw HTTP calls in WahaService.cs:120
var response = await _httpClient.PostAsJsonAsync("/api/sendText", wahaMessage, cancellationToken);
```

**What Should Be:**
```csharp
// ✅ CORRECT: Using WAHA SDK
var result = await _wahaClient.SendTextMessageAsync(sessionName, chatId, message, cancellationToken);
```

### Immediate Actions Required:
1. **ServiceCollectionExtensions.cs**: Add `services.AddWahaApiClient("Waha")`
2. **WahaService.cs**: Inject and use `IWahaApiClient` instead of raw `HttpClient`
3. **Configuration**: Ensure proper WAHA SDK configuration section
4. **Tests**: Update tests to verify WAHA SDK integration

---

## Task 2: WAHA Integration Service Implementation [🔧 NEEDS CRITICAL FIXES - Currently BROKEN]
- [🔧] **Implement core WAHA service with WAHA SDK (CRITICAL FIX NEEDED)**
  - [x] Create `IWahaService` interface with required methods
  - [❌] Implement `WahaService` class with WAHA SDK client (CURRENTLY USING RAW HTTP)
  - [x] Add phone number validation logic (E.164, international, WAHA chat ID formats)
  - [❌] Implement message sending using WAHA SDK methods (CURRENTLY USING RAW HTTP CALLS)
  - [❌] Add session validation using WAHA SDK (CURRENTLY USING RAW HTTP)
  - [🆕] **CRITICAL**: Replace raw HttpClient usage with IWahaApiClient from WAHA NuGet package
  - **Requirements:**
    - [❌] Requirement 2: WAHA Integration Service (BROKEN - NOT USING WAHA SDK)
  - **Tests:**
    - [x] Test 1: Verify phone number validation accepts valid formats
    - [x] Test 2: Verify phone number validation rejects invalid formats
    - [❌] Test 3: Verify WAHA SDK client sends correct payloads (CURRENTLY TESTS RAW HTTP)
    - [❌] **Unit tests need to be updated to test WAHA SDK integration**

- [🔧] **Add retry policies and error handling (NEEDS SDK INTEGRATION)**
  - [❌] Configure WAHA SDK client properly (CURRENTLY USING MANUAL HTTP CLIENT)
  - [❌] Leverage WAHA SDK's built-in retry mechanisms instead of manual Polly policies
  - [❌] Use WAHA SDK's error handling instead of manual HTTP status code mapping
  - [x] Create custom exceptions (`WahaServiceException`) for different error scenarios
  - [x] Add comprehensive logging for all operations
  - [🆕] **CRITICAL**: Replace manual HTTP resilience with WAHA SDK's built-in resilience
  - **Requirements:**
    - [❌] Requirement 7: Error Handling and Resilience (NEEDS WAHA SDK INTEGRATION)
  - **Tests:**
    - [❌] Test 1: Verify WAHA SDK retry logic works properly
    - [❌] Test 2: Verify WAHA SDK error handling integrates with our custom exceptions
    - [❌] Test 3: Verify WAHA SDK authentication and session management
    - [❌] **Resilience testing needs to be updated for WAHA SDK integration**

- [x] **Create WAHA message models and response handling**
  - [x] Implement `WahaMessage` model for sending messages
  - [x] Implement `WahaMessageResult` model for API responses
  - [x] Add JSON serialization attributes and converters (System.Text.Json)
  - [x] Implement response validation and error parsing
  - [x] Add IsSuccess computed property and status code mapping
  - **Requirements:**
    - [x] Requirement 2: WAHA Integration Service
  - **Tests:**
    - [x] Test 1: Verify models serialize to correct JSON format
    - [x] Test 2: Verify response models deserialize WAHA API responses
    - [x] Test 3: Verify error responses are properly parsed
    - [x] **Full model serialization/deserialization test coverage**

---

## Task 3: ntfy Service Implementation (Message Relay + Notifications) [✅ 100% Complete]
- [x] **Implement ntfy polling service for message retrieval**
  - [x] Create `INtfyService` interface with polling and notification methods
  - [x] Implement HTTP client for polling ntfy topics via GET requests
  - [x] Add polling method to retrieve messages from ntfy JSON API
  - [x] Implement message deduplication tracking with processed IDs
  - [x] Add graceful handling of ntfy service unavailability
  - **Requirements:**
    - [x] Requirement 3: Phase 2 - Message Receiver (revised for ntfy polling)
    - [x] Requirement 5: ntfy Notification Integration
  - **Tests:**
    - [x] Test 1: Verify polling retrieves messages from correct ntfy topic
    - [x] Test 2: Verify message deduplication prevents duplicate processing
    - [x] Test 3: Verify graceful handling when ntfy service is down
    - [x] **Added comprehensive unit tests in NtfyServiceTests.cs with 43 passing tests**

- [x] **Implement ntfy notification service**
  - [x] Add notification methods for different event types (success, error, info)
  - [x] Implement fire-and-forget pattern to avoid blocking main processing
  - [x] Add support for multiple ntfy topics (messages vs notifications)
  - [x] Create structured message formatting for monitoring events
  - **Requirements:**
    - [x] Requirement 5: ntfy Notification Integration
  - **Tests:**
    - [x] Test 1: Verify notifications are sent without blocking main thread
    - [x] Test 2: Verify ntfy unavailability doesn't affect core processing
    - [x] Test 3: Verify notification messages have correct format
    - [x] **Fire-and-forget pattern tests with configurable enable/disable**

- [x] **Create ntfy models and configuration**
  - [x] Implement `NtfyMessage` model for API responses
  - [x] Create `NtfyPollingResponse` model for batch message retrieval
  - [x] Add `MessageProcessingContext` model for deduplication tracking
  - [x] Update `NtfySettings` with polling configuration (interval, topics)
  - **Requirements:**
    - [x] Requirement 8: Configuration Management
  - **Tests:**
    - [x] Test 1: Verify ntfy models serialize/deserialize correctly
    - [x] Test 2: Verify polling configuration is loaded properly
    - [x] Test 3: Verify message context tracking works correctly
    - [x] **Added comprehensive model tests in NtfyModelsTests.cs with 38 passing tests**

---

## Task 4: Phase 1 - Hello World Console Application [✅ 100% Complete]
- [x] **Implement command-line argument parsing**
  - [x] Create `HelloWorldService` class for main logic
  - [x] Add command-line argument validation (phone number required)
  - [x] Implement help text and usage instructions
  - [x] Add support for configuration overrides via command line
  - **Requirements:**
    - [x] Requirement 1: Phase 1 - Hello World Message Sender
  - **Tests:**
    - [x] Test 1: Verify application shows help when no arguments provided
    - [x] Test 2: Verify phone number validation rejects invalid inputs
    - [x] Test 3: Verify command-line configuration overrides work
    - [x] **Added comprehensive argument validation tests with help flags support**

- [x] **Implement hello world message sending logic**
  - [x] Integrate with `IWahaService` for message sending
  - [x] Add "hello world" message formatting with app info and timestamp
  - [x] Implement success/failure logging with structured Serilog logging
  - [x] Add proper exit codes (0 for success, non-zero for errors)
  - [x] Include ntfy notification for successful sends and error reporting
  - **Requirements:**
    - [x] Requirement 1: Phase 1 - Hello World Message Sender
    - [x] Requirement 5: ntfy Notification Integration
  - **Tests:**
    - [x] Test 1: Verify "hello world" message is sent to correct number
    - [x] Test 2: Verify application exits with code 0 on success
    - [x] Test 3: Verify application exits with non-zero code on failure
    - [x] **Full message content validation with app info inclusion**

- [x] **Add configuration and error handling**
  - [x] Set up `appsettings.json` with WAHA and ntfy configuration
  - [x] Add environment variable support for sensitive settings
  - [x] Implement comprehensive error handling with user-friendly messages
  - [x] Add validation for WAHA session availability
  - [x] Added graceful Ctrl+C handling and proper cleanup
  - [x] Added file and console logging with configurable levels
  - **Requirements:**
    - [x] Requirement 8: Configuration Management
    - [x] Requirement 7: Error Handling and Resilience
  - **Tests:**
    - [x] Test 1: Verify configuration loads from appsettings.json
    - [x] Test 2: Verify environment variables override file settings
    - [x] Test 3: Verify clear error messages for common issues
    - [x] **Added comprehensive unit tests in HelloWorldServiceTests.cs with 25 passing tests**

---

## Task 5: Phase 2 - Message Receiving Infrastructure
- [ ] **Implement ntfy polling service**
  - [ ] Create `NtfyPollingService` for HTTP GET polling
  - [ ] Add polling configuration (interval, topic name, max messages)
  - [ ] Implement message deduplication using processed message IDs
  - [ ] Add error handling for ntfy service unavailability
  - [ ] Parse ntfy JSON response into message objects
  - **Requirements:**
    - [ ] Requirement 3: Phase 2 - Message Receiver (revised for polling)
  - **Tests:**
    - [ ] Test 1: Verify polling retrieves messages from ntfy topic
    - [ ] Test 2: Verify message deduplication prevents reprocessing
    - [ ] Test 3: Verify graceful handling when ntfy is unavailable

- [ ] **Create ntfy message models and WAHA payload extraction**
  - [ ] Implement `NtfyMessage` model for ntfy API response
  - [ ] Create `NtfyPollingResponse` model for batch message retrieval
  - [ ] Add parsing logic to extract WAHA webhook payload from ntfy message
  - [ ] Implement validation for extracted webhook payloads
  - **Requirements:**
    - [ ] Requirement 3: Phase 2 - Message Receiver (revised)
  - **Tests:**
    - [ ] Test 1: Verify ntfy messages deserialize correctly
    - [ ] Test 2: Verify WAHA webhook payload extraction works
    - [ ] Test 3: Verify validation handles malformed messages gracefully

- [ ] **Set up background polling service**
  - [ ] Create `MessagePollingBackgroundService` using HostedService
  - [ ] Implement configurable polling loop with cancellation support
  - [ ] Add concurrent message processing within polling batches
  - [ ] Implement exponential backoff for polling errors
  - **Requirements:**
    - [ ] Requirement 3: Phase 2 - Message Receiver (revised)
  - **Tests:**
    - [ ] Test 1: Verify background service starts and stops correctly
    - [ ] Test 2: Verify polling loop respects configured interval
    - [ ] Test 3: Verify error handling doesn't stop polling service

---

## Task 6: Echo Response Message Processing
- [ ] **Implement echo message processor**
  - [ ] Create `EchoMessageProcessor` implementing `IMessageProcessor`
  - [ ] Add contact name extraction from webhook payload
  - [ ] Implement echo message formatting: "[contact] said [message]"
  - [ ] Add fallback to phone number when contact name unavailable
  - [ ] Handle empty messages with appropriate response
  - **Requirements:**
    - [ ] Requirement 4: Echo Response Generator
  - **Tests:**
    - [ ] Test 1: Verify echo format is correct with contact name
    - [ ] Test 2: Verify fallback to phone number when name unavailable
    - [ ] Test 3: Verify empty message handling works correctly

- [ ] **Integrate echo processor with WAHA service**
  - [ ] Add response message sending via `IWahaService`
  - [ ] Implement error handling for send failures
  - [ ] Add retry logic for message sending
  - [ ] Include success/failure logging
  - **Requirements:**
    - [ ] Requirement 4: Echo Response Generator
    - [ ] Requirement 2: WAHA Integration Service
  - **Tests:**
    - [ ] Test 1: Verify echo responses are sent back to original sender
    - [ ] Test 2: Verify retry logic works for failed sends
    - [ ] Test 3: Verify proper error handling when WAHA unavailable

- [ ] **Add ntfy notifications for message processing**
  - [ ] Send notification when message is received
  - [ ] Send notification when echo response is sent successfully
  - [ ] Send error notification when processing fails
  - [ ] Include message summary and processing time in notifications
  - **Requirements:**
    - [ ] Requirement 5: ntfy Notification Integration
  - **Tests:**
    - [ ] Test 1: Verify notifications are sent for message processing events
    - [ ] Test 2: Verify error notifications include relevant details
    - [ ] Test 3: Verify notifications don't block message processing

---

## Task 7: Configuration and Environment Setup
- [ ] **Create comprehensive configuration system**
  - [ ] Implement environment-specific `appsettings.json` files
  - [ ] Add configuration validation with data annotations
  - [ ] Support environment variable overrides for all settings
  - [ ] Add configuration documentation and examples
  - **Requirements:**
    - [ ] Requirement 8: Configuration Management
  - **Tests:**
    - [ ] Test 1: Verify configuration validation catches invalid settings
    - [ ] Test 2: Verify environment-specific overrides work
    - [ ] Test 3: Verify all required settings have appropriate defaults

- [ ] **Set up Docker configuration for development**
  - [ ] Create `docker-compose.yml` for WAHA service
  - [ ] Configure WAHA webhook URL to point to ntfy topic
  - [ ] Add environment configuration for local development
  - [ ] Create setup documentation for WAHA QR code scanning and webhook setup
  - [ ] Add health checks and service dependencies
  - **Requirements:**
    - [ ] Requirement 2: WAHA Integration Service
  - **Tests:**
    - [ ] Test 1: Verify Docker compose starts WAHA successfully
    - [ ] Test 2: Verify WAHA webhook is configured to send to ntfy topic
    - [ ] Test 3: Verify framework can connect to dockerized WAHA
    - [ ] Test 4: Verify webhook messages appear in ntfy topic

- [ ] **Create deployment configurations**
  - [ ] Add production-ready configuration templates
  - [ ] Create Dockerfile for message receiving service
  - [ ] Add Kubernetes deployment manifests (optional)
  - [ ] Document deployment procedures and requirements
  - **Requirements:**
    - [ ] Requirement 8: Configuration Management
  - **Tests:**
    - [ ] Test 1: Verify Docker image builds successfully
    - [ ] Test 2: Verify production configuration is secure
    - [ ] Test 3: Verify deployment documentation is complete

---

## Task 8: Testing Infrastructure and Coverage
- [ ] **Set up unit testing framework**
  - [ ] Configure xUnit with test projects
  - [ ] Add Moq for mocking external dependencies
  - [ ] Set up test configuration and helpers
  - [ ] Configure code coverage reporting
  - **Requirements:**
    - [ ] All requirements (testing ensures quality)
  - **Tests:**
    - [ ] Test 1: Verify all test projects run successfully
    - [ ] Test 2: Verify mocking framework works correctly
    - [ ] Test 3: Verify code coverage reports are generated

- [ ] **Create integration tests**
  - [ ] Set up test WAHA instance for integration testing
  - [ ] Create end-to-end tests for hello world functionality
  - [ ] Add ntfy polling integration tests
  - [ ] Test ntfy integration with real and mock services
  - **Requirements:**
    - [ ] All requirements (integration validation)
  - **Tests:**
    - [ ] Test 1: Verify end-to-end hello world flow works
    - [ ] Test 2: Verify ntfy polling and message processing flow works
    - [ ] Test 3: Verify error scenarios are handled correctly

- [ ] **Add performance and load testing**
  - [ ] Create ntfy polling throughput tests
  - [ ] Add memory usage and leak detection for long-running polling
  - [ ] Test concurrent message processing performance
  - [ ] Benchmark polling intervals and response times
  - **Requirements:**
    - [ ] Requirement 3: Phase 2 - Message Receiver (revised for polling)
    - [ ] Requirement 7: Error Handling and Resilience
  - **Tests:**
    - [ ] Test 1: Verify polling can handle target message throughput
    - [ ] Test 2: Verify no memory leaks during extended polling operation
    - [ ] Test 3: Verify polling intervals and response times meet targets

---

## Task 9: Documentation and Extensibility Framework
- [ ] **Create comprehensive documentation**
  - [ ] Write setup and installation guide
  - [ ] Document configuration options and examples
  - [ ] Create troubleshooting guide for common issues
  - [ ] Add API documentation for future extensibility
  - **Requirements:**
    - [ ] Requirement 9: Extensibility for AI Medical Assistant
  - **Tests:**
    - [ ] Test 1: Verify setup guide works for new developers
    - [ ] Test 2: Verify configuration examples are accurate
    - [ ] Test 3: Verify troubleshooting guide covers common scenarios

- [ ] **Design extensibility interfaces for AI integration**
  - [ ] Create pluggable message processor interface
  - [ ] Add support for multiple response generators
  - [ ] Design conversation context storage interface
  - [ ] Plan session management for multi-user scenarios
  - **Requirements:**
    - [ ] Requirement 9: Extensibility for AI Medical Assistant
  - **Tests:**
    - [ ] Test 1: Verify new message processors can be added easily
    - [ ] Test 2: Verify response generator interface supports AI integration
    - [ ] Test 3: Verify architecture supports future conversation storage

- [ ] **Create sample extensions and examples**
  - [ ] Build sample AI message processor (placeholder)
  - [ ] Create example configuration for production deployment
  - [ ] Add monitoring and observability examples
  - [ ] Document scaling and performance considerations
  - **Requirements:**
    - [ ] Requirement 9: Extensibility for AI Medical Assistant
  - **Tests:**
    - [ ] Test 1: Verify sample AI processor integrates correctly
    - [ ] Test 2: Verify production examples are realistic
    - [ ] Test 3: Verify monitoring examples provide useful insights

---

## Implementation Priority Order

### Phase 1 Priority (Minimum Viable Product)
1. **Task 1**: Project Setup and Core Infrastructure
2. **Task 2**: WAHA Integration Service Implementation  
3. **Task 3**: ntfy Notification Service Implementation
4. **Task 4**: Phase 1 - Hello World Console Application
5. **Task 7**: Configuration and Environment Setup (basic)

### Phase 2 Priority (Echo Responder via Message Receiving)
6. **Task 5**: Phase 2 - Message Receiving Infrastructure
7. **Task 6**: Echo Response Message Processing
8. **Task 8**: Testing Infrastructure and Coverage

### Phase 3 Priority (Production Ready)
9. **Task 7**: Configuration and Environment Setup (complete)
10. **Task 9**: Documentation and Extensibility Framework

## Dependencies
- **Task 2** and **Task 3** depend on **Task 1** (infrastructure)
- **Task 4** depends on **Task 2** and **Task 3** (services)
- **Task 5** and **Task 6** depend on **Task 2** (WAHA service)
- **Task 8** can run in parallel with development tasks
- **Task 9** depends on completion of core functionality

## Success Criteria
- ✅ Phase 1: Successfully send "hello world" message via console command
- ✅ Phase 2: Successfully receive WhatsApp messages via ntfy polling and send echo responses
- ✅ WAHA webhook configured to send messages to ntfy topic
- ✅ ntfy polling service retrieves and processes messages with deduplication
- ✅ ntfy notifications working for all major monitoring events
- ✅ Comprehensive test coverage (>80%) including polling scenarios
- ✅ Complete documentation for setup and usage (including WAHA webhook configuration)
- ✅ Architecture ready for AI medical assistant extension
