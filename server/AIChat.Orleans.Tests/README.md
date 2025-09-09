# AIChat.Orleans.Tests

Comprehensive test suite for AIChat Orleans components, covering all three phases of the migration from SSE to Orleans-based architecture.

## Test Structure

### Phase 1: Shadow Mode Tests
- **UserGrainTests**: Core grain functionality and state management
- **OrleansIntegrationServiceTests**: Service layer integration and feature flag behavior
- Focus on shadow mode operations that don't affect existing SSE system

### Phase 2: SignalR Integration Tests (Future)
- SignalR hub integration with Orleans
- Connection management and routing
- Multi-tab synchronization

### Phase 3: Background Processing Tests (Future)
- Background service coordination
- Operation cancellation and tracking
- Complete SSE replacement validation

## Running Tests

### Prerequisites
- .NET 9.0 SDK
- Orleans TestingHost packages

### Command Line
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=UserGrainTests"

# Run with verbose output
dotnet test --verbosity normal

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
```

### Visual Studio
- Use Test Explorer to run individual tests
- Set breakpoints for debugging Orleans grain behavior
- Use Live Unit Testing for continuous testing during development

## Test Categories

### Unit Tests
- **Grain Logic**: Individual grain method behavior
- **Service Integration**: Orleans client service functionality
- **State Management**: Grain state persistence and lifecycle
- **Error Handling**: Exception handling in shadow mode

### Integration Tests
- **TestCluster Integration**: Full Orleans cluster testing
- **Multi-Grain Operations**: Concurrent grain interactions
- **Persistence Testing**: State survival across grain reactivation
- **Health Monitoring**: Health check functionality

### Performance Tests
- **Concurrent Users**: Multiple grains operating simultaneously
- **Activity Buffer**: Circular buffer performance under load
- **Memory Usage**: Grain state size optimization
- **Cleanup Operations**: Timer-based cleanup efficiency

## Key Test Scenarios

### Phase 1 Shadow Mode
```csharp
[Test]
public async Task UserGrain_ShouldRecordActivity_InShadowMode()
{
    // Test that activities are recorded without affecting main system
    var grain = _cluster.GrainFactory.GetGrain<IUserGrain>("test-user");
    await grain.RecordActivity(ActivityType.MessageSent, "metadata");
    
    var state = await grain.GetState();
    Assert.That(state.Metrics.TotalActivities, Is.EqualTo(1));
}
```

### Feature Flag Testing
```csharp
[Test]
public async Task OrleansService_WithFeatureFlagDisabled_ShouldNotCallGrain()
{
    // Test that feature flags properly control Orleans usage
    _mockFeatureManager.Setup(x => x.IsEnabledAsync("OrleansIntegration"))
                      .ReturnsAsync(false);
    
    await _service.RecordUserActivityAsync("user", ActivityType.Connected, data);
    
    _mockGrain.Verify(x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()), 
                     Times.Never);
}
```

### Error Resilience Testing
```csharp
[Test]
public async Task ShadowMode_ShouldNeverThrowExceptions()
{
    // Verify that Orleans failures don't affect main application
    _mockGrain.Setup(x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()))
              .ThrowsAsync(new Exception("Orleans failure"));
    
    Assert.DoesNotThrowAsync(async () => {
        await _service.RecordUserActivityAsync("user", ActivityType.MessageSent, data);
    });
}
```

## Test Data Management

### Test Users
- Unique user IDs for each test to avoid state conflicts
- Realistic activity patterns for integration testing
- Concurrent user scenarios for performance testing

### Mock Configuration
- Proper mocking of IGrainFactory for unit tests
- IFeatureManager mocking for feature flag testing
- ILogger verification for diagnostic testing

### Test Cluster Setup
```csharp
[SetUp]
public async Task Setup()
{
    var builder = new TestClusterBuilder();
    builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
    
    _cluster = builder.Build();
    await _cluster.DeployAsync();
}
```

## Continuous Integration

### Build Pipeline Integration
- Tests run automatically on every commit
- Coverage reports generated and tracked
- Performance benchmarks recorded
- Orleans cluster health verified

### Test Configuration
- Separate test configurations for different environments
- Memory-based storage for fast test execution
- Console logging for CI/CD diagnostics

## Debugging Tests

### Common Issues
1. **Grain Activation Timeouts**: Increase test timeouts for slow builds
2. **State Persistence**: Ensure proper test cleanup between runs
3. **Mock Verification**: Check that mocks are properly configured
4. **TestCluster Lifecycle**: Verify proper setup/teardown

### Debugging Tips
```csharp
// Enable detailed Orleans logging in tests
builder.ConfigureLogging(logging => {
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

// Add diagnostic output in tests
[Test]
public async Task DiagnosticTest()
{
    var state = await grain.GetState();
    TestContext.WriteLine($"Grain state: {JsonSerializer.Serialize(state)}");
}
```

## Performance Benchmarks

### Target Metrics (Phase 1)
- **Grain Activation**: < 50ms
- **Activity Recording**: < 5ms
- **State Retrieval**: < 10ms
- **Health Checks**: < 100ms
- **Concurrent Operations**: 100+ users simultaneously

### Load Testing
```csharp
[Test]
public async Task LoadTest_100ConcurrentUsers()
{
    var tasks = Enumerable.Range(0, 100)
        .Select(i => SimulateUserActivity($"load-user-{i}"))
        .ToArray();
    
    var stopwatch = Stopwatch.StartNew();
    await Task.WhenAll(tasks);
    stopwatch.Stop();
    
    Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000));
}
```

## Future Test Expansions

### Phase 2 Additions
- SignalR connection lifecycle testing
- Message routing verification
- Multi-tab synchronization validation
- Protocol negotiation testing

### Phase 3 Additions
- Background processing validation
- Operation cancellation testing
- SSE removal verification
- End-to-end performance testing

## Contributing to Tests

### Best Practices
1. **Test Isolation**: Each test should be independent
2. **Descriptive Names**: Test names should describe the scenario
3. **Arrange-Act-Assert**: Follow AAA pattern consistently
4. **Mock Verification**: Verify all mock interactions
5. **Error Testing**: Test both success and failure paths

### Code Coverage Goals
- **Grain Logic**: 95% coverage
- **Service Integration**: 90% coverage
- **Error Handling**: 100% coverage
- **Edge Cases**: 85% coverage

This test suite ensures that the Orleans integration is robust, reliable, and ready for production deployment while maintaining backward compatibility with the existing SSE system.