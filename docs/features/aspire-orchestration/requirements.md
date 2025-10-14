# Microsoft Aspire Orchestration - Requirements Document

**Feature**: Adopt Microsoft Aspire for Service Orchestration
**Status**: Requirements Approved
**Date**: 2025-10-13
**Author**: Claude (spec-writer agent)

## Executive Summary

Replace the current manual PowerShell script-based service orchestration with Microsoft Aspire to provide a simplified, unified developer experience. The primary goal is to enable developers to start the entire application stack (database, Orleans silo, backend server, frontend client) with a single command (F5 or `dotnet run`).

## Reference Documents

- [Comprehensive Aspire Research](../../../scratchpad/aspire-research/comprehensive-aspire-research.md)
- Current Project Architecture Analysis (from research phase)

---

## 1. Goals and Success Criteria

### Primary Goals

1. **Simplified Developer Experience**
   - Replace two PowerShell scripts with single application launcher
   - Enable F5 debugging of entire application stack
   - Eliminate manual orchestration complexity

2. **Single Application Launcher**
   - One command starts all services in correct order
   - Automatic dependency management
   - Proper health check coordination

### Success Criteria

✅ **Developer Onboarding**
- New developer can clone repo and start entire system with one command
- No manual configuration of service URLs or ports required
- Clear documentation of Aspire workflow

✅ **Operational Reliability**
- Services start in correct order with dependency checks
- Orleans silo available before AIChat.Server connects
- Database schema initialized before services start

✅ **Development Workflow**
- F5 in Visual Studio starts all services
- Aspire dashboard shows unified logs and traces
- Service discovery "just works" (no hardcoded URLs)

✅ **Code Quality**
- Zero warnings in build
- No breaking changes to existing service functionality
- All existing tests pass

---

## 2. Scope

### In Scope

#### Services to Orchestrate

1. **SQLite Database** (Aspire-managed)
   - Persistent data directory binding
   - Schema initialization coordination
   - Optional SQLiteWeb UI for inspection

2. **AIChat.Orleans.Host** (New Separate Service)
   - Orleans silo with monitoring API
   - Localhost clustering (development mode)
   - Memory-based grain storage
   - **CRITICAL CHANGE**: Remove Orleans Host background task from AIChat.Server

3. **AIChat.Server** (Backend API)
   - ASP.NET Core 9.0 with SignalR
   - REST API endpoints
   - SSE/WebSocket handlers
   - Orleans **client only** (connects to separate silo)
   - Health checks and metrics

4. **SvelteKit Client** (Frontend)
   - Node.js/npm-based frontend
   - Automatic server URL configuration
   - Hot reload support

#### AppHost Projects

1. **DocProject.AppHost**
   - Orchestration logic
   - Service dependency definitions
   - Configuration injection
   - Launch profiles for different scenarios

2. **DocProject.ServiceDefaults**
   - Shared OpenTelemetry configuration
   - Health check setup
   - Logging configuration
   - Service discovery helpers

### Out of Scope (Explicitly NOT Doing)

❌ **Redis Integration** - Using localhost Orleans clustering for simplicity
❌ **Load Testing Integration** - AIChat.LoadTesting remains separate manual tool
❌ **Docker Compose Migration** - Focus on Aspire, leave docker-compose.yml as-is
❌ **Database Migration** - Keeping SQLite, not moving to PostgreSQL/SQL Server
❌ **Production Deployment** - This iteration focuses on development experience
❌ **CI/CD Pipeline Changes** - Existing build/test pipelines unchanged

---

## 3. Architecture Decisions

### Decision 1: Orleans Host as Separate Service ✅

**Decision**: Remove Orleans Host background task from AIChat.Server and make it a separate service orchestrated by Aspire.

**Rationale**:
- Cleaner separation of concerns
- Better service isolation
- Independent lifecycle management
- Easier debugging
- More production-like architecture

**Impact**:
- **BREAKING CHANGE**: AIChat.Server Program.cs must be modified to remove Orleans Host startup
- AIChat.Server becomes Orleans **client only**
- Communication remains over localhost network
- Service startup order becomes: Database → Orleans Host → AIChat.Server → Client

### Decision 2: Full Configuration Injection ✅

**Decision**: Use Aspire's full configuration injection for service URLs, connection strings, and service references.

**Rationale**:
- Maximum benefit from Aspire's service discovery
- Eliminates hardcoded URLs and connection strings
- Single source of truth in AppHost
- Easier environment configuration

**Impact**:
- AppHost manages all service-to-service communication config
- appsettings files reduced to business logic configuration
- Service URLs automatically injected via environment variables
- Orleans connection strings managed by Aspire

### Decision 3: Localhost Orleans Clustering ✅

**Decision**: Continue using `UseLocalhostClustering()` for Orleans, no Redis integration.

**Rationale**:
- Simplicity for development workflow
- Reduces infrastructure requirements
- Sufficient for current development needs
- Can add Redis later without changing architecture

**Impact**:
- Orleans state lost on restart (acceptable for dev)
- Single-silo development setup
- No persistent clustering storage needed

### Decision 4: Aspire-Managed SQLite ✅

**Decision**: Use Aspire's SQLite integration with data binding for persistent storage.

**Rationale**:
- Aspire handles SQLite lifecycle
- Persistent data across restarts
- Optional SQLiteWeb UI for inspection
- Clean integration pattern

**Impact**:
- Database files managed by Aspire (in designated directory)
- Connection strings injected by Aspire
- Test environment continues using in-memory SQLite
- Schema initialization remains in service code

### Decision 5: Temporary PowerShell Script Retention ✅

**Decision**: Keep PowerShell scripts temporarily during transition period (1-2 sprints), then deprecate.

**Rationale**:
- Safety net during migration
- Allows gradual team adoption
- Rollback option if issues discovered
- Smoother change management

**Impact**:
- Maintain both startup methods temporarily
- Document Aspire as primary, scripts as legacy
- Add deprecation warnings to scripts
- Plan script removal after team validates Aspire

### Decision 6: Single-Iteration Migration ✅

**Decision**: Complete entire Aspire adoption in one implementation iteration.

**Rationale**:
- Fastest time to value
- Avoids extended dual-maintenance period
- Clear before/after state
- Simplified testing and validation

**Impact**:
- All changes delivered together
- Comprehensive testing required before merge
- Team switches to Aspire workflow immediately after merge
- Higher upfront effort but lower total cost

---

## 4. Technical Requirements

### 4.1 AppHost Project

**DocProject.AppHost** must:

1. Define all service resources:
   - SQLite database with data binding
   - AIChat.Orleans.Host as ASP.NET project
   - AIChat.Server as ASP.NET project
   - SvelteKit client as npm app

2. Configure service dependencies:
   ```
   Client → Server → Orleans Host → Database
   ```

3. Inject configuration:
   - Service URLs (automatic)
   - Connection strings (database, Orleans)
   - Environment variables (LLM_API_KEY, etc.)

4. Provide launch profiles:
   - Default: Start all services
   - Server-only: Just backend (for backend dev)
   - Frontend-only: Client + mock server (future)

5. Enable Aspire dashboard:
   - Accessible at http://localhost:15888
   - Show all service logs
   - Display distributed traces
   - Expose metrics

### 4.2 ServiceDefaults Project

**DocProject.ServiceDefaults** must provide:

1. OpenTelemetry configuration:
   - Distributed tracing setup
   - Metrics collection
   - Log correlation

2. Health check defaults:
   - Standard health check patterns
   - Dependency health checks

3. Service discovery helpers:
   - HttpClient configuration with service discovery
   - Orleans client configuration with service discovery

4. Logging configuration:
   - Structured logging setup
   - Correlation ID propagation

### 4.3 Service Updates

#### AIChat.Server Changes (CRITICAL)

**Must Remove**:
- Orleans Host background task startup code
- Internal DI container for Orleans Host
- Orleans silo configuration
- Any silo-related lifecycle management

**Must Keep**:
- Orleans **client** configuration
- Connection to external Orleans silo (via Aspire-injected connection string)
- All existing API endpoints
- SignalR hub
- Health checks

**Must Add**:
- Reference to DocProject.ServiceDefaults
- Call to `builder.AddServiceDefaults()`
- Orleans client health check dependency

#### AIChat.Orleans.Host Changes

**Must Add**:
- Reference to DocProject.ServiceDefaults
- Call to `builder.AddServiceDefaults()`
- Ensure monitoring API endpoints are functional

**Must Verify**:
- Silo starts independently
- Monitoring endpoints accessible
- Health checks report correctly

#### SvelteKit Client Changes

**Must Add**:
- Environment variable consumption for server URL (VITE_API_URL)
- Documentation of Aspire-injected variables

**Must Verify**:
- Automatic server URL configuration works
- Hot reload continues to function
- npm scripts compatible with Aspire

### 4.4 Configuration Management

**AppHost Configuration** (`appsettings.json` in AppHost):

```json
{
  "Aspire": {
    "Dashboard": {
      "Enabled": true,
      "Port": 15888
    }
  },
  "LLM_API_KEY": "DUMMY"  // Default for development
}
```

**Service Configuration Strategy**:
- Service URLs: Aspire-injected (no appsettings entry)
- Connection strings: Aspire-injected (no appsettings entry)
- Business logic config: Remains in service appsettings files
- Secrets: User secrets for local, Aspire configuration for deployment

### 4.5 Development Workflow

**Starting Application**:
```bash
# Option 1: Visual Studio
F5 in DocProject.AppHost project

# Option 2: Command line
cd DocProject.AppHost
dotnet run

# Option 3: PowerShell (temporary, deprecated)
.\build-and-start-server.ps1
.\build-and-start-client.ps1
```

**Accessing Services**:
- **Aspire Dashboard**: http://localhost:15888
- **AIChat.Server API**: http://localhost:5099 (or Aspire-assigned)
- **Orleans Monitoring**: http://localhost:5100 (or Aspire-assigned)
- **SvelteKit Client**: http://localhost:5173 (or Aspire-assigned)
- **SQLite Web UI**: http://localhost:8080 (optional)

**Debugging**:
- Attach debugger to any service from Visual Studio
- Breakpoints work across all services
- Distributed traces show request flow

---

## 5. Backward Compatibility

### During Transition Period (1-2 Sprints)

**PowerShell Scripts**:
- ✅ Continue to function as before
- ⚠️ Add deprecation warnings in script output
- 📝 Update documentation to prefer Aspire
- ⏰ Plan removal date and communicate to team

**Configuration Files**:
- ✅ Existing appsettings.json files continue to work
- ✅ Environment variables continue to work
- 🆕 Aspire-injected values override appsettings (when running via AppHost)

**Service Functionality**:
- ✅ All existing API endpoints unchanged
- ✅ SignalR hub behavior unchanged
- ✅ Orleans grain interfaces unchanged
- ✅ Database schema unchanged

### Breaking Changes (Documented)

1. **Orleans Host Removal from AIChat.Server**
   - Background task code removed
   - Server is now Orleans client only
   - Must run via Aspire or start Orleans Host separately

2. **Service URLs**
   - Hardcoded localhost URLs replaced with Aspire-injected
   - Running services standalone (outside Aspire) requires manual config

3. **Startup Scripts**
   - Aspire becomes primary method
   - PowerShell scripts deprecated after validation period

---

## 6. Acceptance Criteria

### Functional Acceptance

✅ **F1: Single Command Startup**
- Running `dotnet run` in AppHost starts all services
- Services start in correct dependency order
- Health checks pass before dependent services start

✅ **F2: Service Discovery**
- Client automatically knows server URL (no hardcoded config)
- Server automatically connects to Orleans Host (no hardcoded config)
- Configuration changes in AppHost propagate automatically

✅ **F3: Orleans Separation**
- AIChat.Orleans.Host runs as independent process
- AIChat.Server successfully connects as Orleans client
- All Orleans functionality continues to work (UserGrain, ChatGrain, etc.)

✅ **F4: Database Initialization**
- SQLite schema created before services start
- Data persists across Aspire restarts
- Test environment continues using in-memory database

✅ **F5: Aspire Dashboard**
- Dashboard accessible at http://localhost:15888
- All service logs visible and filterable
- Distributed traces show request flow across services
- Metrics displayed correctly

### Non-Functional Acceptance

✅ **NF1: Build Quality**
- Zero build warnings
- Zero static analysis warnings
- All existing unit tests pass
- All existing integration tests pass

✅ **NF2: Performance**
- Startup time ≤ 30 seconds for all services
- No performance degradation vs PowerShell scripts
- Debugging overhead acceptable

✅ **NF3: Documentation**
- README updated with Aspire instructions
- `.instructions/` updated with Aspire workflow
- Deprecation notice added to PowerShell scripts
- Aspire dashboard usage documented

✅ **NF4: Code Quality**
- SOLID principles maintained
- No circular dependencies introduced
- Proper error handling and logging
- Inline code documentation added

---

## 7. Testing Strategy

### Unit Testing

**Scope**: Individual service functionality unchanged
- All existing unit tests must pass without modification
- No new unit tests required (no business logic changes)

### Integration Testing

**Scope**: Service-to-service communication via Aspire

**Test Scenarios**:
1. **Database Initialization**
   - SQLite schema created correctly
   - Data binding persists across restarts
   - Test environment uses in-memory database

2. **Orleans Connectivity**
   - Server successfully connects to Orleans Host
   - Grains accessible from server
   - Monitoring endpoints respond correctly

3. **Service Discovery**
   - Client receives correct server URL
   - Server receives correct Orleans connection string
   - Configuration changes propagate

4. **Startup Order**
   - Database ready before Orleans Host starts
   - Orleans Host ready before Server starts
   - Server ready before Client starts

5. **Health Checks**
   - All services report healthy
   - Aspire dashboard shows correct status
   - Dependent services wait for dependencies

### Manual Testing

**Test Plan**:
1. Fresh clone of repository
2. Install prerequisites (.NET 9, Node.js, Aspire workload)
3. `dotnet run` in AppHost
4. Verify all services start
5. Open Aspire dashboard, verify logs/traces
6. Test chat functionality end-to-end
7. Stop and restart, verify data persists
8. Test debugging workflow (F5, breakpoints)

### Regression Testing

**Critical Paths**:
- User authentication
- Chat message sending/receiving
- Orleans grain activation
- SignalR connection management
- SSE streaming
- MCP integration (if enabled)

---

## 8. Risks and Mitigation

### Risk 1: Orleans Separation Complexity

**Risk**: Removing Orleans Host from AIChat.Server might break subtle dependencies

**Probability**: Medium
**Impact**: High

**Mitigation**:
- Thorough code review of AIChat.Server Program.cs
- Test Orleans client connectivity extensively
- Keep PowerShell scripts as rollback option
- Validate all grain calls still work

### Risk 2: Configuration Injection Failures

**Risk**: Aspire-injected configuration might not cover all edge cases

**Probability**: Medium
**Impact**: Medium

**Mitigation**:
- Comprehensive testing of all configuration scenarios
- Fallback to appsettings if Aspire values missing
- Clear error messages when configuration incomplete
- Document manual configuration for standalone service execution

### Risk 3: Team Adoption Resistance

**Risk**: Developers might resist switching from familiar PowerShell scripts

**Probability**: Low
**Impact**: Low

**Mitigation**:
- Keep scripts temporarily during transition
- Demonstrate clear value (single F5, unified dashboard)
- Provide training and documentation
- Address concerns proactively

### Risk 4: Startup Time Regression

**Risk**: Aspire might add overhead to startup time

**Probability**: Low
**Impact**: Low

**Mitigation**:
- Measure baseline startup time before migration
- Compare after migration
- Optimize if necessary (parallel service starts)
- Accept minor overhead for significant DX gains

### Risk 5: SQLite Data Binding Issues

**Risk**: Aspire-managed SQLite might have path/permission issues

**Probability**: Low
**Impact**: Medium

**Mitigation**:
- Use well-tested data binding directory
- Verify read/write permissions
- Test on multiple developer machines
- Document SQLite file locations clearly

---

## 9. Rollback Plan

### Immediate Rollback (During Implementation)

If critical issues discovered during implementation:

1. **Don't merge PR** - Keep changes in feature branch
2. **Continue using PowerShell scripts** - No disruption to team
3. **Debug and fix** - Resolve issues before merge
4. **Re-test** - Validate fixes before second attempt

### Post-Merge Rollback (After Team Adoption)

If critical issues discovered after merge:

1. **Revert commit** - Git revert to before Aspire adoption
2. **Communicate to team** - Notify of temporary rollback
3. **Emergency fix** - Address critical issues in hotfix branch
4. **Re-deploy** - Merge hotfix and re-attempt migration

**Rollback Criteria** (triggers immediate rollback):
- Application won't start
- Data loss or corruption
- Critical functionality broken (auth, chat, Orleans)
- Build failures unresolved within 2 hours

---

## 10. Implementation Phases

### Phase 1: Project Setup (Estimated: 1 hour)

**Tasks**:
1. Install Aspire workload (`dotnet workload install aspire`)
2. Create DocProject.AppHost project
3. Create DocProject.ServiceDefaults project
4. Add projects to solution file
5. Configure basic AppHost Program.cs structure

**Deliverables**:
- Empty AppHost and ServiceDefaults projects in solution
- Projects build successfully

### Phase 2: AppHost Configuration (Estimated: 2 hours)

**Tasks**:
1. Define SQLite resource with data binding
2. Add AIChat.Orleans.Host project reference
3. Add AIChat.Server project reference
4. Add SvelteKit client npm app reference
5. Configure service dependencies (WaitFor)
6. Inject configuration (connection strings, env vars)
7. Set up launch profiles

**Deliverables**:
- Complete AppHost Program.cs
- Services start in correct order
- Aspire dashboard accessible

### Phase 3: ServiceDefaults Implementation (Estimated: 1 hour)

**Tasks**:
1. Implement AddServiceDefaults() extension method
2. Configure OpenTelemetry (tracing, metrics, logging)
3. Set up default health checks
4. Configure service discovery helpers

**Deliverables**:
- ServiceDefaults project with AddServiceDefaults() method
- Observability infrastructure ready

### Phase 4: AIChat.Server Refactoring (Estimated: 2 hours)

**CRITICAL TASKS**:
1. Remove Orleans Host background task code
2. Remove internal DI container for silo
3. Convert to Orleans client only
4. Add ServiceDefaults reference and call
5. Update Orleans client configuration for service discovery
6. Test Orleans connectivity to separate silo

**Deliverables**:
- AIChat.Server is Orleans client only (no silo hosting)
- Successfully connects to AIChat.Orleans.Host
- All existing functionality works

### Phase 5: Service Integration (Estimated: 2 hours)

**Tasks**:
1. Update AIChat.Orleans.Host with ServiceDefaults
2. Update client environment variable consumption
3. Test service-to-service communication
4. Verify health checks
5. Test debugging workflow (F5)

**Deliverables**:
- All services integrated with Aspire
- Service discovery working
- End-to-end functionality validated

### Phase 6: Testing & Documentation (Estimated: 2 hours)

**Tasks**:
1. Run all unit tests (must pass)
2. Run all integration tests (must pass)
3. Perform manual regression testing
4. Update README with Aspire instructions
5. Update `.instructions/` for Aspire workflow
6. Add deprecation warnings to PowerShell scripts
7. Document Aspire dashboard usage

**Deliverables**:
- All tests passing
- Complete documentation
- PowerShell scripts marked deprecated

**Total Estimated Time**: 10 hours (1.25 days)

---

## 11. Success Metrics

### Quantitative Metrics

**Development Efficiency**:
- Startup command reduction: 2 scripts → 1 command ✅ (100% improvement)
- Time to start all services: Measure baseline vs Aspire (target: ≤30s)
- Lines of orchestration code: PowerShell scripts vs AppHost (target: -50%)

**Code Quality**:
- Build warnings: 0 (maintained)
- Test pass rate: 100% (maintained)
- Static analysis violations: 0 (maintained)

**Developer Experience**:
- Onboarding time for new developer: Measure before/after (target: -50%)
- Context switches during debugging: Reduced (unified dashboard)
- Configuration errors: Reduced (service discovery eliminates manual URLs)

### Qualitative Metrics

**Team Feedback**:
- Developer satisfaction with new workflow (survey after 1 week)
- Number of Aspire-related issues reported
- Time spent on orchestration debugging (vs previous approach)

**Documentation Quality**:
- Completeness of Aspire documentation
- Clarity of migration instructions
- Effectiveness of deprecation communication

---

## 12. Dependencies

### External Dependencies

**Required Software**:
- .NET 9.0 SDK (already in use)
- Aspire workload (new requirement)
- Node.js 18+ (already in use)
- Visual Studio 2022 17.9+ or VS Code with C# extension

**NuGet Packages** (to be added):
- `Aspire.Hosting.AppHost` (AppHost project)
- `Aspire.Hosting.Orleans` (AppHost project)
- `Aspire.Hosting.NodeJS` (AppHost project)
- `CommunityToolkit.Aspire.Hosting.SQLite` (AppHost project)
- `Microsoft.Extensions.ServiceDiscovery` (ServiceDefaults project)

### Internal Dependencies

**Projects to Modify**:
- AIChat.Server (CRITICAL: Orleans Host removal)
- AIChat.Orleans.Host (minor: ServiceDefaults integration)
- Solution file (add AppHost and ServiceDefaults)

**Projects Unchanged**:
- AIChat.Orleans (grains library)
- AIChat.Orleans.Client (client library)
- LmDotnetTools.* (LLM integration libraries)
- Client (SvelteKit app - only config changes)

### Team Dependencies

**Required Skills**:
- Basic understanding of Aspire concepts (training session)
- Familiarity with service orchestration concepts
- Understanding of service discovery patterns

**Team Size**: Minimal impact, suitable for solo implementation

---

## 13. Monitoring and Observability

### Aspire Dashboard

**Access**: http://localhost:15888

**Features to Utilize**:
1. **Resources View**: See all services and their status
2. **Console Logs**: Unified logs from all services
3. **Structured Logs**: Filter by service, level, or text
4. **Distributed Traces**: Follow requests across service boundaries
5. **Metrics**: Request rates, error rates, latencies
6. **Environment**: View injected configuration per service

### OpenTelemetry Integration

**Trace Context Propagation**:
- HTTP requests (Client → Server)
- SignalR messages (Client ↔ Server)
- Orleans grain calls (Server → Grains)

**Metrics to Collect**:
- HTTP request duration
- SignalR connection count
- Orleans grain activation count
- Database query duration

**Logs to Collect**:
- Structured logs with correlation IDs
- Service-specific context (grain ID, user ID)
- Error logs with stack traces

---

## 14. Non-Goals (Explicit)

These items are **explicitly out of scope** to maintain focus:

❌ **Production Deployment Configuration**
- Focus is on development experience
- Production Aspire configuration deferred to future work

❌ **Docker/Kubernetes Integration**
- Keep existing docker-compose.yml as-is
- No container orchestration changes

❌ **Database Migration**
- Keep SQLite (no move to PostgreSQL/SQL Server)
- Schema migration strategy unchanged

❌ **Redis Integration**
- Stick with localhost Orleans clustering
- Redis can be added later without rearchitecture

❌ **Advanced Observability**
- No Grafana dashboard creation
- No Jaeger/Prometheus deployment
- Use Aspire dashboard only

❌ **Load Testing Integration**
- AIChat.LoadTesting remains separate manual tool
- No integration into AppHost

❌ **CI/CD Pipeline Changes**
- Existing build/test pipelines unchanged
- Aspire-specific CI/CD deferred to future work

❌ **Multi-Environment Configuration**
- Focus on local development environment
- Production/staging configuration deferred

❌ **Performance Optimization**
- Accept baseline Aspire performance
- No premature optimization

---

## Approval and Sign-Off

**Requirements Captured By**: Claude (spec-writer agent)
**Requirements Based On**: User responses to clarifying questions
**Date**: 2025-10-13

**Key Decisions Approved**:
- ✅ Orleans Host as separate service (remove from AIChat.Server)
- ✅ Full Aspire configuration injection
- ✅ Localhost Orleans clustering (no Redis)
- ✅ Aspire-managed SQLite with data binding
- ✅ PowerShell scripts kept temporarily
- ✅ Single-iteration migration

**Next Steps**:
1. **Design Phase**: spec-architect agent will create detailed technical design
2. **Task Breakdown**: spec-planner agent will create actionable task list
3. **Implementation**: task-senior-developer agent will implement following SOLID principles

---

**Document Version**: 1.0
**Status**: Approved and Ready for Design Phase
