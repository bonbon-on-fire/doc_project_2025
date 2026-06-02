# User Feedback and Learnings

## High-Level Purpose (Collected)
- **Primary Purpose**: Tool/framework for an AI-powered medical assistant that communicates via WhatsApp
- **Current Scope**: Just the messaging framework component - raw and functional only
- **Future Integration**: Will be used within a larger AI medical assistant project
- **Users**: Patients/users who will interact with the medical AI assistant through WhatsApp
- **Focus**: Keep it simple and functional, not a full-featured product yet

## Key Requirements Identified
- Send messages to WhatsApp users
- Receive messages from WhatsApp users  
- Use WPPConnect server tool (not cloud API)
- No frontend needed
- Raw and functional implementation
- Framework/tool for larger product integration

## Technical Requirements (Collected)

### Sending Capabilities
- Text messages ✅
- Images ✅
- Documents ✅

### Receiving Capabilities  
- Text messages ✅
- Images ✅
- Documents ❌ (not needed)

### Message Features
- Plain text conversation only
- No WhatsApp-specific features (buttons, lists, etc.)

### Data Handling
- Log all messages ✅
- Store conversation history ✅
- Prepare for AI system integration

## Additional Requirements (Collected)

### Database & Storage
- **Database Preference**: Requested pros/cons comparison (provided)
- **Logging Detail**: All conversation info from both sides in chronological order
- **Integration Readiness**: Keep it open for future AI integration
- **Environment**: Local machine initially
- **Interface**: Node.js without web interface

### Technical Preferences
- Simple Node.js script (no web interface needed)
- Local development environment
- Chronological message storage
- Future AI integration capability
