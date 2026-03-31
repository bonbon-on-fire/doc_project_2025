# Feature Specification: WhatsApp WAHA Messaging Framework

## High-Level Overview
A .NET Core 9.0 messaging framework that integrates with WAHA (WhatsApp HTTP API) to send and receive WhatsApp messages, serving as the foundation for an AI-powered medical assistant chatbot. The framework includes two phases: a simple message sender and an echo responder with ntfy notifications for monitoring.

## High Level Requirements
1. **Phase 1**: Create a console application that sends "hello world" message to a specified phone number via WAHA
2. **Phase 2**: Implement webhook receiver that echoes incoming messages in format "[contact] said [echo]"
3. **Integration**: Use WAHA for WhatsApp communication and ntfy for monitoring notifications
4. **Technology**: Built with .NET Core 9.0 using modern patterns and best practices
5. **Architecture**: Modular design that can evolve into an AI medical assistant platform

## Existing Solutions
- **WAHA (WhatsApp HTTP API)**: Self-hosted Docker-based solution ([waha.devlike.pro](https://waha.devlike.pro/))
- **ntfy**: HTTP-based pub-sub notification service ([ntfy.sh](https://ntfy.sh/))
- **WhatsApp Cloud API**: Official Meta/Facebook API (alternative considered)
- **Twilio WhatsApp API**: Third-party service (alternative considered)

## Current Implementation
- **Starting Point**: Empty workspace with git repository
- **Infrastructure**: No existing components
- **Setup Required**: WAHA Docker container, .NET project creation, ntfy integration

## Detailed Requirements

### Requirement 1: Phase 1 - Hello World Message Sender
**User Story**: As a developer, I want to send a "hello world" message to a specific WhatsApp number using a one-time console command, so that I can verify the WAHA integration is working.

#### Acceptance Criteria:
1. **WHEN** I run the console application with a phone number parameter **THEN** it **SHALL** send "hello world" message to that number via WAHA API
2. **WHEN** the message is sent successfully **THEN** the application **SHALL** log success message and exit with code 0
3. **WHEN** the WAHA API is unavailable **THEN** the application **SHALL** log error message and exit with non-zero code
4. **WHEN** invalid phone number is provided **THEN** the application **SHALL** validate format and show error message
5. **WHEN** WAHA session is not authenticated **THEN** the application **SHALL** provide clear error message about QR code scanning requirement

### Requirement 2: WAHA Integration Service
**User Story**: As a developer, I want a service that communicates with WAHA API, so that I can send WhatsApp messages reliably with proper error handling.

#### Acceptance Criteria:
1. **WHEN** sending a message **THEN** the service **SHALL** make HTTP POST request to WAHA's `/api/sendText` endpoint
2. **WHEN** WAHA returns success response **THEN** the service **SHALL** return success status with message ID
3. **WHEN** WAHA returns error response **THEN** the service **SHALL** throw appropriate exception with error details
4. **WHEN** WAHA is unreachable **THEN** the service **SHALL** handle timeout and connection errors gracefully
5. **WHEN** configuring WAHA connection **THEN** the service **SHALL** read base URL and session from configuration

### Requirement 3: Phase 2 - Webhook Message Receiver
**User Story**: As a developer, I want to receive WhatsApp messages via webhook, so that I can process incoming messages and send automated responses.

#### Acceptance Criteria:
1. **WHEN** WAHA sends a webhook with incoming message **THEN** the application **SHALL** receive and parse the webhook payload
2. **WHEN** webhook payload is valid **THEN** the application **SHALL** extract contact information and message content
3. **WHEN** webhook payload is invalid **THEN** the application **SHALL** log error and return appropriate HTTP status
4. **WHEN** webhook endpoint is called **THEN** the application **SHALL** respond with HTTP 200 status within 5 seconds
5. **WHEN** multiple webhooks arrive simultaneously **THEN** the application **SHALL** process them concurrently without blocking

### Requirement 4: Echo Response Generator
**User Story**: As a user sending messages to the WhatsApp number, I want to receive an echo of my message in format "[contact] said [echo]", so that I know the system is working.

#### Acceptance Criteria:
1. **WHEN** a WhatsApp message is received **THEN** the application **SHALL** format response as "[contact] said [original message]"
2. **WHEN** contact name is available **THEN** the application **SHALL** use the contact name instead of phone number
3. **WHEN** contact name is not available **THEN** the application **SHALL** use phone number as contact identifier
4. **WHEN** original message is empty **THEN** the application **SHALL** respond with "[contact] sent an empty message"
5. **WHEN** sending echo response **THEN** the application **SHALL** use WAHA API to send the formatted message back to sender

### Requirement 5: ntfy Notification Integration
**User Story**: As a system administrator, I want to receive notifications about message processing activities, so that I can monitor the system's operation.

#### Acceptance Criteria:
1. **WHEN** a WhatsApp message is received **THEN** the application **SHALL** send notification to ntfy topic with message summary
2. **WHEN** an echo response is sent successfully **THEN** the application **SHALL** notify ntfy with success message
3. **WHEN** an error occurs in message processing **THEN** the application **SHALL** send error notification to ntfy
4. **WHEN** sending ntfy notification **THEN** the application **SHALL** not block message processing if ntfy is unavailable
5. **WHEN** configuring ntfy **THEN** the application **SHALL** read topic URL from configuration settings

### Requirement 6: .NET Core 9.0 Modern Architecture
**User Story**: As a developer, I want the application built with modern .NET patterns, so that it's maintainable and extensible for future AI features.

#### Acceptance Criteria:
1. **WHEN** application starts **THEN** it **SHALL** use dependency injection container for service management
2. **WHEN** logging events **THEN** it **SHALL** use structured logging with Serilog or Microsoft.Extensions.Logging
3. **WHEN** handling configuration **THEN** it **SHALL** use IConfiguration with appsettings.json and environment variables
4. **WHEN** making HTTP requests **THEN** it **SHALL** use typed HttpClient with proper error handling
5. **WHEN** processing async operations **THEN** it **SHALL** use async/await patterns throughout

### Requirement 7: Error Handling and Resilience
**User Story**: As a system operator, I want the application to handle errors gracefully, so that temporary failures don't crash the system.

#### Acceptance Criteria:
1. **WHEN** WAHA API is temporarily unavailable **THEN** the application **SHALL** implement retry logic with exponential backoff
2. **WHEN** webhook processing fails **THEN** the application **SHALL** log error details and continue processing other webhooks
3. **WHEN** ntfy notifications fail **THEN** the application **SHALL** log warning but not affect core message processing
4. **WHEN** configuration is invalid **THEN** the application **SHALL** show clear error messages and exit gracefully
5. **WHEN** unexpected exceptions occur **THEN** the application **SHALL** log full exception details for debugging

### Requirement 8: Configuration Management
**User Story**: As a deployment engineer, I want to configure the application without modifying code, so that I can deploy to different environments easily.

#### Acceptance Criteria:
1. **WHEN** configuring WAHA connection **THEN** settings **SHALL** include base URL, session name, and timeout values
2. **WHEN** configuring ntfy integration **THEN** settings **SHALL** include topic URL and optional authentication
3. **WHEN** configuring webhook server **THEN** settings **SHALL** include port, host binding, and webhook path
4. **WHEN** running in different environments **THEN** configuration **SHALL** support environment-specific overrides
5. **WHEN** sensitive data is required **THEN** configuration **SHALL** support environment variables for secrets

### Requirement 9: Extensibility for AI Medical Assistant
**User Story**: As a product developer, I want the framework designed for easy extension, so that I can add AI medical assistant features in the future.

#### Acceptance Criteria:
1. **WHEN** designing message processing **THEN** architecture **SHALL** support pluggable message handlers
2. **WHEN** implementing responses **THEN** system **SHALL** support different response generators (echo, AI, etc.)
3. **WHEN** adding new integrations **THEN** architecture **SHALL** support additional notification channels beyond ntfy
4. **WHEN** scaling the system **THEN** design **SHALL** support multiple WhatsApp sessions and load balancing
5. **WHEN** adding AI features **THEN** message processing pipeline **SHALL** support async AI service calls

## Technical Architecture

### System Components
```
┌─────────────────┐    ┌──────────────┐    ┌───────────────┐
│   WhatsApp      │    │    WAHA      │    │  .NET Core    │
│   Messages      │◄──►│   Docker     │◄──►│  Application  │
│                 │    │   Container  │    │               │
└─────────────────┘    └──────────────┘    └───────┬───────┘
                                                   │
                                                   ▼
                                           ┌───────────────┐
                                           │     ntfy      │
                                           │ Notifications │
                                           └───────────────┘
```

### Phase Implementation
- **Phase 1**: Console App → WAHA API (direct HTTP calls)
- **Phase 2**: ASP.NET Core webhook endpoint + WAHA API client + ntfy integration

### Technology Stack
- **.NET Core 9.0**: Application framework
- **WAHA**: WhatsApp HTTP API (Docker: `devlikeapro/waha`)
- **ntfy**: Notification service (https://ntfy.sh)
- **Docker**: WAHA container hosting
- **HTTP APIs**: RESTful communication
- **JSON**: Data exchange format
