# Orpheus Test Suite

This project contains unit tests and integration tests for the Orpheus Discord bot.

## Test Structure

- **Unit Tests**: Test individual components in isolation
  - `QueuedSongTests.cs` - Tests for the QueuedSong model
  - `SongQueueServiceTests.cs` - Tests for the song queue service
  - `BotConfigurationTests.cs` - Tests for configuration handling

- **Integration Tests**: Test component interactions and system setup
  - `DependencyInjectionTests.cs` - Tests for service registration and DI setup
  - `DockerBuildTests.cs` - Tests for Docker build validation

## Running Tests

### Command Line
```bash
# Run all tests
dotnet test

# Run tests with verbose output
dotnet test --verbosity normal

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "ClassName=QueuedSongTests"

# Run tests by category/namespace
dotnet test --filter "FullyQualifiedName~Orpheus.Tests.Queue"
```

### From Solution Root
```bash
# Run all tests in the solution
dotnet test Orpheus.sln

# Build and test everything
dotnet build && dotnet test
```

## Test Coverage

The test suite covers:
- ✅ Core models (QueuedSong)
- ✅ Business logic services (SongQueueService, BotConfiguration)
- ✅ Dependency injection setup
- ✅ Docker build validation
- ⏳ Service mocking patterns (for services with external dependencies)

## Adding New Tests

1. Create test files following the naming convention: `[ClassName]Tests.cs`
2. Use the same namespace structure as the code being tested: `Orpheus.Tests.[Namespace]`
3. Follow AAA pattern (Arrange, Act, Assert) in test methods
4. Use descriptive test method names: `MethodName_Condition_ExpectedResult`
5. Add `[Fact]` for simple tests or `[Theory]` with `[InlineData]` for parameterized tests

## Dependencies

- **xUnit**: Test framework
- **Moq**: Mocking framework for dependencies
- **Microsoft.NET.Test.Sdk**: Test platform
- **coverlet.collector**: Code coverage collection

## Notes

- Tests are configured to run in parallel by default
- Some integration tests may be skipped if external dependencies (like Docker) are not available
- Mock objects are used to isolate units under test from external dependencies