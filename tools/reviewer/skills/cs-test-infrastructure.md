---
name: cs-test-infrastructure-review
description: >-
  Review C# test code for correctness, reliability, and design defects:
  assertions on wrong variables, async void test methods, tests with no
  assertions, floating-point equality without tolerance, execution-order
  dependencies, mock setup mismatches, system clock dependencies, network
  dependencies, Thread.Sleep for synchronization, over-mocking, and missing
  IDisposable cleanup in test fixtures.
  20 patterns across 3 sub-domains covering test-correctness,
  test-reliability, and test-design. Covers xUnit, NUnit, and MSTest
  patterns. Sources include xUnit documentation, NUnit best practices,
  MSTest framework guidance, and common CI/CD failure analysis.
  Use this skill when reviewing C# test code using xUnit, NUnit, MSTest,
  Moq, NSubstitute, or other .NET test infrastructure.
---

# C# Test Infrastructure Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- Test methods returning `void` with `async` keyword (framework doesn't await)
- Test methods with no `Assert` / `Should` / `Verify` calls (always passes)
- `Assert.Equal` on `float`/`double` without precision/tolerance parameter
- `Thread.Sleep` in test methods (flaky timing-dependent synchronization)
- Mock setup with specific arguments that don't match actual call patterns
- `[Fact]` / `[Test]` / `[TestMethod]` on `private` or `internal` methods (never discovered)
- Tests reading from network, file system, or `DateTime.Now` without abstraction
- Shared mutable state (static fields) between test methods

**Key Code Patterns to Search For**:
```csharp
// Async void test — framework doesn't await, test always "passes"
[Fact]
public async void Should_LoadData()  // BUG: should be async Task
{
    var result = await _service.LoadAsync();
    Assert.NotNull(result);  // This assertion may never execute
}

// Test with no assertions — always passes
[Fact]
public void Should_ProcessOrder()
{
    var service = new OrderService();
    service.Process(new Order());
    // No assertion — what exactly is being tested?
}

// Thread.Sleep for synchronization — flaky
[Fact]
public async Task Should_CompleteBackgroundWork()
{
    _service.StartBackgroundWork();
    Thread.Sleep(2000);  // Hope 2 seconds is enough?
    Assert.True(_service.IsComplete);
}
```

## Analysis Workflow

### Step 1: Identify Test Framework and Infrastructure

Determine the test framework(s) and mocking libraries in use.

1. Check for framework indicators:
   - **xUnit**: `[Fact]`, `[Theory]`, `[InlineData]`, `IClassFixture<T>`
   - **NUnit**: `[Test]`, `[TestCase]`, `[TestFixture]`, `[SetUp]`, `[TearDown]`
   - **MSTest**: `[TestMethod]`, `[TestClass]`, `[TestInitialize]`, `[TestCleanup]`
   - **Mocking**: `Mock<T>` (Moq), `Substitute.For<T>` (NSubstitute), `A.Fake<T>` (FakeItEasy)

2. Identify test project structure: unit tests, integration tests, end-to-end tests.

3. Build a **Test Infrastructure Map**:
   | Project | Framework | Mocking | Test Count | Integration? |
   |---------|-----------|---------|------------|-------------|
   | `MyApp.Tests` | xUnit | Moq | 142 | No |
   | `MyApp.IntegrationTests` | xUnit | None | 23 | Yes |

### Step 2: Scan for Pattern Matches

Apply the 20 test infrastructure patterns across 3 sub-domains.

**Priority order** (by impact):
1. **Test Correctness** (8 patterns) — tests that appear to pass but don't actually verify anything
2. **Test Reliability** (6 patterns) — flaky tests that fail intermittently on CI
3. **Test Design** (6 patterns) — maintenance burden, misleading test results

**Key detection queries per category**:

| Sub-domain | What to Look For | Risk |
|-----------|-----------------|------|
| Async void | `async void` test methods | Critical — test always "passes" |
| No assertions | Test methods without Assert/Should/Verify | High — false confidence |
| Wrong assertion target | Assert on input/setup variable, not result | High — doesn't test anything |
| Mock mismatch | `Setup(x => x.Method("specific"))` but called with different args | High — false positive |
| Thread.Sleep | `Thread.Sleep` or `Task.Delay` for synchronization | High — flaky |
| Shared state | Static mutable fields in test classes | High — order-dependent |

### Step 3: Classify Findings

For each potential match:

1. **Confirm the defect**: Is the test actually broken?
   - Does the async void test actually have meaningful assertions after the await?
   - Is the "no assertion" test actually verifying via expected exception (`Assert.Throws`)?
   - Is `Thread.Sleep` used as a deliberate rate-limiter rather than synchronization?
2. **Severity**:
   - **Critical**: Async void test with assertions (appears to pass, assertions never run)
   - **High**: No assertions, wrong assertion target, mock setup mismatch, shared mutable state
   - **Medium**: Floating-point equality, missing edge cases, system clock dependency
   - **Low**: Long test setup, integration test in unit project, style issues

### Step 4: Generate Fix

**Fix Strategy Decision Tree**:

```
What kind of test defect?
├── Correctness
│   ├── Async void → Change return type to Task
│   ├── No assertions → Add meaningful assertion or delete test
│   ├── Wrong assertion target → Assert on actual result, not input
│   ├── Float equality → Use Assert.Equal with precision parameter
│   ├── Mock mismatch → Align Setup arguments with actual call pattern
│   └── Swallowed exception → Use Assert.Throws / Assert.ThrowsAsync
├── Reliability
│   ├── Thread.Sleep → Use async wait with timeout, or TaskCompletionSource
│   ├── System clock → Inject IClock/TimeProvider abstraction
│   ├── Network dependency → Use WireMock/HttpMessageHandler mock
│   ├── File system paths → Use Path.Combine with TestContext.TestDirectory
│   └── Shared mutable state → Use fresh instance per test, or IClassFixture
└── Design
    ├── Over-mocking → Mock dependencies, not the thing under test
    ├── Long setup → Extract to helper method or test fixture
    ├── Missing IDisposable → Implement IDisposable/IAsyncDisposable on fixture
    ├── Private test method → Make public
    └── Integration in unit project → Move to separate integration test project
```

**Fix template**:
```markdown
#### Finding: [Pattern-ID] — [Brief description]
**File**: `path/to/test.cs` lines N-M
**Severity**: [Critical|High|Medium|Low]
**Pattern**: [Pattern ID and name]

**Before** (defective):
```csharp
// Problematic test code
```

**After** (correct):
```csharp
// Fixed test code
```

**Verification**:
- [ ] Test fails when production code has the bug it's meant to catch
- [ ] Test passes reliably on CI (no flaky failures)
- [ ] No shared mutable state between test methods
- [ ] Test framework discovers and runs the test
```

### Step 5: Verify Fix

1. **Mutation check**: Temporarily break the production code the test covers — the test should fail.
2. **CI reliability**: Run the test suite multiple times to verify no flaky failures.
3. **Isolation check**: Run each test individually and in random order — all should pass.
4. **Framework discovery**: Verify `dotnet test --list-tests` includes the test.

---

## Pattern Catalog

### Test Correctness

#### TEST-CORR-01: Test assertion on wrong variable
**Severity**: High — asserts setup value, not result

The test asserts on the input variable or a setup value rather than the actual result of the operation under test. The test passes regardless of what the production code does.

```csharp
// BAD (xUnit): Asserting on setup value instead of result
[Fact]
public void Calculate_WithValidInput_ReturnsExpectedResult()
{
    var input = new CalculationRequest { Value = 42 };
    var calculator = new Calculator();

    var result = calculator.Calculate(input);

    Assert.Equal(42, input.Value);  // BUG: Asserts on input, not result!
    // This always passes regardless of what Calculate() returns
}
```

```csharp
// GOOD: Assert on the actual result
[Fact]
public void Calculate_WithValidInput_ReturnsExpectedResult()
{
    var input = new CalculationRequest { Value = 42 };
    var calculator = new Calculator();

    var result = calculator.Calculate(input);

    Assert.Equal(84, result.Output);  // Asserts on the result of the operation
}
```

---

#### TEST-CORR-02: Async test method returning void instead of Task
**Severity**: Critical — test framework doesn't await

When an async test method returns `void` instead of `Task`, the test framework cannot await it. The test method returns immediately after the first `await`, and any assertions after the await never execute. The test always "passes."

```csharp
// BAD (xUnit): async void — test framework doesn't await, assertions after await are skipped
[Fact]
public async void Should_FetchUserData_FromApi()
{
    var service = new UserService(_mockHttp.Object);

    var user = await service.GetUserAsync(42);  // Method returns here — rest never runs

    Assert.NotNull(user);           // NEVER EXECUTED
    Assert.Equal("Alice", user.Name);  // NEVER EXECUTED
}
```

```csharp
// GOOD (xUnit): async Task — framework awaits, all assertions execute
[Fact]
public async Task Should_FetchUserData_FromApi()
{
    var service = new UserService(_mockHttp.Object);

    var user = await service.GetUserAsync(42);

    Assert.NotNull(user);
    Assert.Equal("Alice", user.Name);  // Executes correctly
}
```

```csharp
// GOOD (NUnit): async Task
[Test]
public async Task Should_FetchUserData_FromApi()
{
    var user = await _service.GetUserAsync(42);
    Assert.That(user, Is.Not.Null);
    Assert.That(user.Name, Is.EqualTo("Alice"));
}
```

```csharp
// GOOD (MSTest): async Task
[TestMethod]
public async Task Should_FetchUserData_FromApi()
{
    var user = await _service.GetUserAsync(42);
    Assert.IsNotNull(user);
    Assert.AreEqual("Alice", user.Name);
}
```

---

#### TEST-CORR-03: Test with no assertions
**Severity**: High — always passes

A test method that exercises code but never asserts anything always passes. It provides false confidence that the feature works.

```csharp
// BAD: No assertion — this test always passes
[Fact]
public void ProcessOrder_WithValidOrder_ShouldSucceed()
{
    var processor = new OrderProcessor(_mockRepo.Object, _mockNotifier.Object);
    var order = CreateTestOrder();

    processor.Process(order);

    // No assertion — what are we testing? Did Process actually do anything correct?
}
```

```csharp
// GOOD: Meaningful assertions verify behavior
[Fact]
public void ProcessOrder_WithValidOrder_SavesAndNotifies()
{
    var processor = new OrderProcessor(_mockRepo.Object, _mockNotifier.Object);
    var order = CreateTestOrder();

    processor.Process(order);

    _mockRepo.Verify(r => r.Save(It.Is<Order>(o => o.Status == OrderStatus.Processed)), Times.Once);
    _mockNotifier.Verify(n => n.SendConfirmation(order.CustomerId), Times.Once);
}
```

---

#### TEST-CORR-04: Assert.Equal with floating-point without tolerance
**Severity**: Medium — flaky

Floating-point arithmetic is inherently imprecise. `Assert.Equal(0.3, 0.1 + 0.2)` fails because `0.1 + 0.2 == 0.30000000000000004`. Without a tolerance parameter, floating-point assertions are unreliable.

```csharp
// BAD (xUnit): Exact floating-point comparison — fails due to precision
[Fact]
public void CalculateDiscount_Returns30Percent()
{
    var result = _calculator.CalculateDiscount(100.0, 0.3);

    Assert.Equal(0.3, result / 100.0);  // FAILS: 0.30000000000000004 != 0.3
}
```

```csharp
// GOOD (xUnit): Use precision parameter (number of decimal places)
[Fact]
public void CalculateDiscount_Returns30Percent()
{
    var result = _calculator.CalculateDiscount(100.0, 0.3);

    Assert.Equal(0.3, result / 100.0, precision: 10);  // Passes: equal within 10 decimal places
}
```

```csharp
// GOOD (NUnit): Use Within tolerance
[Test]
public void CalculateDiscount_Returns30Percent()
{
    var result = _calculator.CalculateDiscount(100.0, 0.3);

    Assert.That(result / 100.0, Is.EqualTo(0.3).Within(1e-10));
}
```

```csharp
// GOOD (MSTest): Use delta parameter
[TestMethod]
public void CalculateDiscount_Returns30Percent()
{
    var result = _calculator.CalculateDiscount(100.0, 0.3);

    Assert.AreEqual(0.3, result / 100.0, delta: 1e-10);
}
```

---

#### TEST-CORR-05: Test depends on execution order
**Severity**: High — shared mutable state between tests

Tests that depend on shared mutable state (static fields, database state) pass when run in a specific order but fail when run individually or in a different order. Test runners do not guarantee execution order.

```csharp
// BAD: Tests share mutable static state — order-dependent
public class UserServiceTests
{
    private static List<User> _sharedUsers = new();  // Static: shared across all tests

    [Fact]
    public void AddUser_IncreasesCount()
    {
        _sharedUsers.Add(new User("Alice"));
        Assert.Single(_sharedUsers);  // Passes only if this runs first
    }

    [Fact]
    public void AddTwoUsers_CountIsTwo()
    {
        _sharedUsers.Add(new User("Bob"));
        Assert.Equal(2, _sharedUsers.Count);  // Depends on AddUser running first!
    }
}
```

```csharp
// GOOD: Each test has its own state — order-independent
public class UserServiceTests
{
    [Fact]
    public void AddUser_IncreasesCount()
    {
        var users = new List<User>();  // Fresh instance per test
        users.Add(new User("Alice"));
        Assert.Single(users);  // Always passes
    }

    [Fact]
    public void AddTwoUsers_CountIsTwo()
    {
        var users = new List<User>();  // Fresh instance per test
        users.Add(new User("Alice"));
        users.Add(new User("Bob"));
        Assert.Equal(2, users.Count);  // Always passes
    }
}
```

---

#### TEST-CORR-06: Theory with InlineData not covering edge cases
**Severity**: Medium

`[Theory]` with `[InlineData]` that only tests "happy path" values misses boundary conditions, null inputs, empty strings, zero, negative numbers, and maximum values where bugs commonly lurk.

```csharp
// BAD: Only happy-path values — misses edge cases
[Theory]
[InlineData("hello", 5)]
[InlineData("world", 5)]
[InlineData("test", 4)]
public void GetLength_ReturnsCorrectLength(string input, int expected)
{
    Assert.Equal(expected, _service.GetLength(input));
}
// Missing: null, empty string, whitespace, very long string, Unicode
```

```csharp
// GOOD: Edge cases covered
[Theory]
[InlineData("hello", 5)]
[InlineData("", 0)]                          // Empty string
[InlineData(" ", 1)]                          // Whitespace
[InlineData("a", 1)]                          // Single character
[InlineData(null, 0)]                         // Null input (if supported)
public void GetLength_ReturnsCorrectLength(string? input, int expected)
{
    Assert.Equal(expected, _service.GetLength(input));
}

// For numeric inputs:
[Theory]
[InlineData(0)]             // Zero
[InlineData(-1)]            // Negative
[InlineData(int.MaxValue)]  // Upper boundary
[InlineData(int.MinValue)]  // Lower boundary
[InlineData(1)]             // Minimal positive
public void ProcessValue_HandlesEdgeCases(int value)
{
    var result = _service.ProcessValue(value);
    Assert.NotNull(result);
}
```

---

#### TEST-CORR-07: Mock setup doesn't match actual call pattern
**Severity**: High — test passes but production fails

The mock is configured with `Setup` for specific arguments, but the production code calls the mocked method with different arguments. The mock returns the default value instead of the configured value, and the test may still pass by coincidence.

```csharp
// BAD: Mock setup with specific string, but production code calls with different format
[Fact]
public void GetUser_ReturnsUserFromRepository()
{
    var mockRepo = new Mock<IUserRepository>();
    mockRepo.Setup(r => r.GetByEmail("alice@example.com"))
            .Returns(new User("Alice"));

    var service = new UserService(mockRepo.Object);

    // Production code normalizes email to lowercase — calls GetByEmail("ALICE@EXAMPLE.COM".ToLower())
    // But the mock is set up for "alice@example.com" which may not match depending on implementation
    var result = service.FindUser("ALICE@EXAMPLE.COM");

    // If production code has a bug and doesn't normalize, the mock returns null
    // and this test might still pass if we only check for non-null
    Assert.NotNull(result);
}
```

```csharp
// GOOD: Use It.IsAny or It.Is with a predicate for flexible matching
[Fact]
public void GetUser_ReturnsUserFromRepository()
{
    var mockRepo = new Mock<IUserRepository>();
    mockRepo.Setup(r => r.GetByEmail(It.Is<string>(
                e => e.Equals("alice@example.com", StringComparison.OrdinalIgnoreCase))))
            .Returns(new User("Alice"));

    var service = new UserService(mockRepo.Object);
    var result = service.FindUser("ALICE@EXAMPLE.COM");

    Assert.NotNull(result);
    Assert.Equal("Alice", result.Name);
    mockRepo.Verify(r => r.GetByEmail(It.IsAny<string>()), Times.Once);
}
```

---

#### TEST-CORR-08: Test catches exception to assert on it but swallows unexpected exceptions
**Severity**: Medium

A try-catch in a test that catches `Exception` to assert on the message can accidentally catch a completely different exception (e.g., `NullReferenceException` from a bug), and the test passes because the catch block runs.

```csharp
// BAD: Catches any exception — might swallow a NullReferenceException from a bug
[Fact]
public void Withdraw_WithInsufficientFunds_ThrowsException()
{
    var account = new BankAccount(100);

    try
    {
        account.Withdraw(200);
        Assert.Fail("Expected exception was not thrown");
    }
    catch (Exception ex)  // Catches ANY exception — even NullReferenceException
    {
        Assert.Contains("insufficient", ex.Message.ToLower());
    }
}
```

```csharp
// GOOD (xUnit): Use Assert.Throws with specific exception type
[Fact]
public void Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = new BankAccount(100);

    var exception = Assert.Throws<InsufficientFundsException>(
        () => account.Withdraw(200));

    Assert.Equal(200, exception.RequestedAmount);
    Assert.Equal(100, exception.AvailableBalance);
}
```

```csharp
// GOOD (NUnit):
[Test]
public void Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = new BankAccount(100);

    var ex = Assert.Throws<InsufficientFundsException>(
        () => account.Withdraw(200));

    Assert.That(ex.RequestedAmount, Is.EqualTo(200));
}
```

```csharp
// GOOD (MSTest):
[TestMethod]
[ExpectedException(typeof(InsufficientFundsException))]
public void Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = new BankAccount(100);
    account.Withdraw(200);
}

// Or use Assert.ThrowsException (preferred over attribute):
[TestMethod]
public void Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = new BankAccount(100);

    var ex = Assert.ThrowsException<InsufficientFundsException>(
        () => account.Withdraw(200));

    Assert.AreEqual(200, ex.RequestedAmount);
}
```

---

### Test Reliability

#### TEST-REL-01: Test depends on system clock / current date
**Severity**: Medium — flaky on CI

Tests that use `DateTime.Now`, `DateTime.UtcNow`, or `DateTimeOffset.Now` produce different results depending on when they run. They can fail at midnight, on DST transitions, or in different time zones on CI.

```csharp
// BAD: Test depends on current time — fails at midnight, DST boundaries, different time zones
[Fact]
public void IsExpired_WithYesterdayDate_ReturnsTrue()
{
    var license = new License { ExpirationDate = DateTime.Now.AddDays(-1) };

    Assert.True(license.IsExpired);  // Flaky: what if test runs exactly at midnight?
}

// BAD: Production code uses DateTime.Now directly
public class License
{
    public DateTime ExpirationDate { get; set; }
    public bool IsExpired => DateTime.Now > ExpirationDate;  // Untestable
}
```

```csharp
// GOOD: Inject time abstraction (TimeProvider in .NET 8+)
public class License
{
    private readonly TimeProvider _timeProvider;

    public License(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTime ExpirationDate { get; set; }
    public bool IsExpired => _timeProvider.GetUtcNow().DateTime > ExpirationDate;
}

// Test with controlled time:
[Fact]
public void IsExpired_WithPastDate_ReturnsTrue()
{
    var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
    var license = new License(fakeTime)
    {
        ExpirationDate = new DateTime(2024, 6, 14)  // Yesterday relative to fake time
    };

    Assert.True(license.IsExpired);  // Deterministic — always passes
}
```

---

#### TEST-REL-02: Test depends on network / external service
**Severity**: High — flaky, slow

Tests that make real HTTP calls, database queries, or API calls to external services fail when the network is down, the service is slow, or rate limits are hit. They also slow down the test suite significantly.

```csharp
// BAD: Real HTTP call — fails when service is down, slow, or rate-limited
[Fact]
public async Task GetWeather_ReturnsCurrentTemperature()
{
    var client = new HttpClient();
    var response = await client.GetAsync("https://api.weather.gov/current");
    var data = await response.Content.ReadFromJsonAsync<WeatherData>();

    Assert.NotNull(data);
    Assert.InRange(data.Temperature, -50, 60);  // Flaky: depends on actual weather
}
```

```csharp
// GOOD: Mock the HTTP handler
[Fact]
public async Task GetWeather_ReturnsCurrentTemperature()
{
    var handler = new Mock<HttpMessageHandler>();
    handler.Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = JsonContent.Create(new WeatherData { Temperature = 22.5 })
        });

    var client = new HttpClient(handler.Object);
    var service = new WeatherService(client);

    var data = await service.GetWeatherAsync();

    Assert.Equal(22.5, data.Temperature);  // Deterministic — no network dependency
}
```

---

#### TEST-REL-03: Thread.Sleep for synchronization
**Severity**: High — flaky timing

Using `Thread.Sleep` to wait for asynchronous work to complete is unreliable. On slow CI agents, the sleep may not be long enough; on fast machines, it wastes time. This is the most common cause of flaky tests.

```csharp
// BAD: Thread.Sleep — too short on slow CI, too long on fast machines
[Fact]
public void BackgroundProcessor_CompletesWork()
{
    var processor = new BackgroundProcessor();
    processor.StartProcessing(testData);

    Thread.Sleep(5000);  // Hope 5 seconds is enough... (it isn't on CI)

    Assert.True(processor.IsComplete);
}
```

```csharp
// GOOD: Use async wait with timeout
[Fact]
public async Task BackgroundProcessor_CompletesWork()
{
    var processor = new BackgroundProcessor();
    processor.StartProcessing(testData);

    // Wait for completion with timeout — fast when it works, fails quickly when broken
    var completed = await WaitForConditionAsync(
        () => processor.IsComplete,
        timeout: TimeSpan.FromSeconds(30));

    Assert.True(completed, "Processor did not complete within timeout");
}

private static async Task<bool> WaitForConditionAsync(
    Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition() && DateTime.UtcNow < deadline)
    {
        await Task.Delay(50);  // Poll frequently, fail fast
    }
    return condition();
}
```

```csharp
// GOOD (alternative): Use TaskCompletionSource for event-driven completion
[Fact]
public async Task BackgroundProcessor_CompletesWork()
{
    var tcs = new TaskCompletionSource<bool>();
    var processor = new BackgroundProcessor();
    processor.Completed += (s, e) => tcs.SetResult(true);

    processor.StartProcessing(testData);

    var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
    Assert.Equal(tcs.Task, completed);  // Completed before timeout
}
```

---

#### TEST-REL-04: Test depending on file system paths that differ per OS/CI
**Severity**: Medium

Hard-coded file paths using backslashes or absolute paths fail on Linux CI agents, macOS runners, or when the repo is cloned to a different directory.

```csharp
// BAD: Hard-coded Windows path — fails on Linux CI
[Fact]
public void LoadConfig_ReadsFromFile()
{
    var config = ConfigLoader.Load(@"C:\TestData\config.json");  // Only works on Windows, at this exact path
    Assert.NotNull(config);
}

// BAD: Relative path with backslashes — fails on Linux
[Fact]
public void LoadConfig_ReadsFromFile()
{
    var config = ConfigLoader.Load(@"TestData\config.json");  // Backslash fails on Linux/macOS
    Assert.NotNull(config);
}
```

```csharp
// GOOD: Use Path.Combine and test context directory
[Fact]
public void LoadConfig_ReadsFromFile()
{
    var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    var configPath = Path.Combine(testDir, "TestData", "config.json");

    var config = ConfigLoader.Load(configPath);

    Assert.NotNull(config);
}

// GOOD: Use temporary directory for write tests
[Fact]
public void SaveConfig_WritesToFile()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    try
    {
        var configPath = Path.Combine(tempDir, "config.json");
        var config = new AppConfig { Theme = "Dark" };

        ConfigSaver.Save(config, configPath);

        Assert.True(File.Exists(configPath));
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

---

#### TEST-REL-05: Shared test fixture with mutable state across tests
**Severity**: High

When using xUnit's `IClassFixture<T>` or NUnit's `[TestFixture]` with `[SetUp]`, shared fixture state that is mutated by tests causes ordering dependencies. Unlike TEST-CORR-05 (static fields), this involves instance-level fixture state that accumulates across tests.

```csharp
// BAD: Fixture state mutated by tests — ordering dependency
public class DatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DatabaseTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void InsertUser_Succeeds()
    {
        _fixture.Database.Insert(new User("Alice"));  // Modifies shared DB state
        Assert.Equal(1, _fixture.Database.UserCount);
    }

    [Fact]
    public void InsertTwoUsers_CountIsTwo()
    {
        _fixture.Database.Insert(new User("Bob"));
        Assert.Equal(2, _fixture.Database.UserCount);  // Depends on InsertUser running first!
    }
}
```

```csharp
// GOOD: Reset fixture state before each test, or use per-test state
public class DatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DatabaseTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _fixture.Database.Reset();  // Clean state for each test
    }

    [Fact]
    public void InsertUser_Succeeds()
    {
        _fixture.Database.Insert(new User("Alice"));
        Assert.Equal(1, _fixture.Database.UserCount);  // Always correct
    }

    [Fact]
    public void InsertTwoUsers_CountIsTwo()
    {
        _fixture.Database.Insert(new User("Alice"));
        _fixture.Database.Insert(new User("Bob"));
        Assert.Equal(2, _fixture.Database.UserCount);  // Self-contained — always correct
    }
}
```

---

#### TEST-REL-06: UI automation test with hard-coded selectors
**Severity**: High — fragile

UI tests that use hard-coded control names, CSS selectors, or XPath expressions break whenever the UI changes layout, renames controls, or updates text content.

```csharp
// BAD: Hard-coded selectors — break on any UI change
[Fact]
public void ClickSubmitButton_ShowsConfirmation()
{
    var button = _app.FindElement(By.Name("btnSubmitOrder_v2"));  // Breaks on rename
    button.Click();

    var message = _app.FindElement(By.XPath("//div[@class='modal']/p[1]"));  // Fragile XPath
    Assert.Equal("Order submitted successfully!", message.Text);  // Breaks on text change
}
```

```csharp
// GOOD: Use stable identifiers and flexible matching
[Fact]
public void ClickSubmitButton_ShowsConfirmation()
{
    var button = _app.FindElement(By.AutomationId("SubmitOrderButton"));  // Stable automation ID
    button.Click();

    var message = _app.FindElement(By.AutomationId("ConfirmationMessage"));  // Stable automation ID
    Assert.Contains("submitted", message.Text, StringComparison.OrdinalIgnoreCase);  // Flexible text match
}
```

---

### Test Design

#### TEST-DES-01: Test verifying implementation details instead of behavior
**Severity**: Medium

Tests that verify internal method calls, field values, or implementation sequences rather than observable behavior break when the implementation is refactored, even if the behavior remains correct.

```csharp
// BAD: Tests implementation details — breaks on refactoring
[Fact]
public void ProcessPayment_CallsInternalMethodsInOrder()
{
    var mockProcessor = new Mock<PaymentProcessor>() { CallBase = true };
    var order = CreateTestOrder();

    mockProcessor.Object.ProcessPayment(order);

    // Verifying internal method call sequence — implementation detail
    mockProcessor.Verify(p => p.ValidateCard(), Times.Once);
    mockProcessor.Verify(p => p.ReserveAmount(), Times.Once);
    mockProcessor.Verify(p => p.ChargeCard(), Times.Once);
    mockProcessor.Verify(p => p.SendReceipt(), Times.Once);
}
```

```csharp
// GOOD: Test observable behavior and outcomes
[Fact]
public void ProcessPayment_WithValidOrder_ChargesCorrectAmountAndSendsReceipt()
{
    var mockGateway = new Mock<IPaymentGateway>();
    var mockNotifier = new Mock<INotificationService>();
    var processor = new PaymentProcessor(mockGateway.Object, mockNotifier.Object);
    var order = CreateTestOrder(amount: 99.99m);

    var result = processor.ProcessPayment(order);

    Assert.True(result.IsSuccessful);
    Assert.Equal(99.99m, result.ChargedAmount);
    mockGateway.Verify(g => g.Charge(It.Is<decimal>(a => a == 99.99m)), Times.Once);
    mockNotifier.Verify(n => n.SendReceipt(order.CustomerId), Times.Once);
}
```

---

#### TEST-DES-02: Over-mocking — mocking the thing being tested
**Severity**: High

When the class under test is itself mocked (with `CallBase = true`), the test may not exercise real production code. Mocks should be used for dependencies, not for the system under test.

```csharp
// BAD: Mocking the system under test — not testing real code
[Fact]
public void Calculator_Add_ReturnsSum()
{
    var mockCalc = new Mock<Calculator> { CallBase = true };
    // This partially mocks the calculator itself — confusing and fragile

    mockCalc.Setup(c => c.Validate(It.IsAny<int>())).Returns(true);

    var result = mockCalc.Object.Add(2, 3);

    Assert.Equal(5, result);
    // But wait — is Validate actually being tested? Is Add using real code?
}
```

```csharp
// GOOD: Mock dependencies, test the real class
[Fact]
public void Calculator_Add_ReturnsSum()
{
    var mockValidator = new Mock<IInputValidator>();
    mockValidator.Setup(v => v.Validate(It.IsAny<int>())).Returns(true);

    var calculator = new Calculator(mockValidator.Object);  // Real calculator, mocked dependency

    var result = calculator.Add(2, 3);

    Assert.Equal(5, result);
    mockValidator.Verify(v => v.Validate(2), Times.Once);
    mockValidator.Verify(v => v.Validate(3), Times.Once);
}
```

---

#### TEST-DES-03: Test setup longer than 20 lines with no helper extraction
**Severity**: Low

When test setup (Arrange) is very long, it obscures what the test actually verifies. The test becomes hard to read and maintain. Extract complex setup into named helper methods.

```csharp
// BAD: 25+ lines of setup obscure the actual test
[Fact]
public void ProcessOrder_WithDiscounts_CalculatesCorrectTotal()
{
    var customer = new Customer
    {
        Id = 1,
        Name = "Alice",
        Email = "alice@example.com",
        MemberSince = new DateTime(2020, 1, 1),
        Tier = CustomerTier.Gold,
        Address = new Address
        {
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701"
        }
    };
    var product1 = new Product { Id = 1, Name = "Widget", Price = 25.00m, Category = "Electronics" };
    var product2 = new Product { Id = 2, Name = "Gadget", Price = 50.00m, Category = "Electronics" };
    var discount = new Discount { Type = DiscountType.Percentage, Value = 10, AppliesTo = "Electronics" };
    var order = new Order
    {
        Customer = customer,
        Items = new List<OrderItem>
        {
            new OrderItem { Product = product1, Quantity = 2 },
            new OrderItem { Product = product2, Quantity = 1 }
        },
        Discounts = new List<Discount> { discount }
    };
    var processor = new OrderProcessor(new Mock<IRepository>().Object);

    var result = processor.CalculateTotal(order);

    Assert.Equal(90.00m, result);  // (25*2 + 50) * 0.9 = 90
}
```

```csharp
// GOOD: Helpers make the test readable — setup, action, and assertion are all clear
[Fact]
public void ProcessOrder_WithDiscounts_CalculatesCorrectTotal()
{
    var order = CreateOrderWithElectronics(widgetQty: 2, gadgetQty: 1)
        .WithDiscount(DiscountType.Percentage, value: 10, category: "Electronics");
    var processor = CreateOrderProcessor();

    var result = processor.CalculateTotal(order);

    Assert.Equal(90.00m, result);  // (25*2 + 50) * 0.9 = 90
}

// Helper methods in the test class:
private static Order CreateOrderWithElectronics(int widgetQty, int gadgetQty) => /* ... */;
private OrderProcessor CreateOrderProcessor() => new(new Mock<IRepository>().Object);
```

---

#### TEST-DES-04: Missing IDisposable cleanup in test fixture
**Severity**: Medium — resource leak across test runs

Test fixtures that create disposable resources (database connections, HTTP clients, temp files) but don't implement `IDisposable` leak resources across the test run, eventually causing failures from exhausted connections or file handles.

```csharp
// BAD: Fixture creates disposable resources but doesn't implement IDisposable
public class ApiTestFixture
{
    public HttpClient Client { get; }
    public SqlConnection Database { get; }

    public ApiTestFixture()
    {
        Client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        Database = new SqlConnection("Server=.;Database=TestDb;Trusted_Connection=True");
        Database.Open();
    }
    // No Dispose — HttpClient and SqlConnection leak!
}
```

```csharp
// GOOD (xUnit): Implement IDisposable (or IAsyncLifetime for async cleanup)
public class ApiTestFixture : IDisposable
{
    public HttpClient Client { get; }
    public SqlConnection Database { get; }

    public ApiTestFixture()
    {
        Client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        Database = new SqlConnection("Server=.;Database=TestDb;Trusted_Connection=True");
        Database.Open();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Database?.Dispose();
    }
}

// GOOD (xUnit async): IAsyncLifetime for async setup/teardown
public class ApiTestFixture : IAsyncLifetime
{
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
        await WaitForServerReadyAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await CleanupDatabaseAsync();
    }
}
```

```csharp
// GOOD (NUnit): [OneTimeTearDown]
[TestFixture]
public class ApiTests
{
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _client?.Dispose();
    }
}
```

---

#### TEST-DES-05: [Fact] / [Test] on private/internal method — test not discovered
**Severity**: High

Test frameworks discover test methods via reflection and require them to be `public`. A `[Fact]`, `[Test]`, or `[TestMethod]` attribute on a `private` or `internal` method is silently ignored by the test runner.

```csharp
// BAD (xUnit): Private test method — never discovered, never runs
public class UserServiceTests
{
    [Fact]
    private void Should_CreateUser_WithValidInput()  // NEVER RUNS — private!
    {
        var service = new UserService();
        var user = service.Create("Alice");
        Assert.NotNull(user);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    internal void Should_ProcessItem(int id)  // NEVER RUNS — internal!
    {
        Assert.True(_service.Process(id));
    }
}
```

```csharp
// GOOD: Test methods must be public
public class UserServiceTests
{
    [Fact]
    public void Should_CreateUser_WithValidInput()  // Discovered and runs
    {
        var service = new UserService();
        var user = service.Create("Alice");
        Assert.NotNull(user);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Should_ProcessItem(int id)  // Discovered and runs
    {
        Assert.True(_service.Process(id));
    }
}
```

```bash
# Verify test discovery:
dotnet test --list-tests
# If a test doesn't appear in the list, check visibility
```

---

#### TEST-DES-06: Integration test mixed in unit test project
**Severity**: Low — slows CI

Integration tests (database, network, file system, external APIs) mixed into a unit test project slow down the entire suite and cause failures when CI agents don't have the required infrastructure. Separating them allows unit tests to run fast on every commit while integration tests run on a schedule.

```
# BAD: All tests in one project — slow CI, infrastructure failures block unit tests
MyApp.Tests/
    UserServiceTests.cs          (unit — fast)
    OrderProcessorTests.cs       (unit — fast)
    DatabaseMigrationTests.cs    (integration — needs SQL Server)
    ApiEndpointTests.cs          (integration — needs running API)
    PaymentGatewayTests.cs       (integration — needs network)
```

```
# GOOD: Separated projects — unit tests run fast, integration tests run on schedule
MyApp.UnitTests/
    UserServiceTests.cs
    OrderProcessorTests.cs

MyApp.IntegrationTests/
    DatabaseMigrationTests.cs
    ApiEndpointTests.cs
    PaymentGatewayTests.cs
```

```csharp
// If separation isn't possible, use categories/traits to filter:

// xUnit: Use [Trait] for categorization
[Trait("Category", "Integration")]
[Fact]
public async Task Database_MigratesSuccessfully()
{
    // ...
}

// NUnit: Use [Category]
[Category("Integration")]
[Test]
public async Task Database_MigratesSuccessfully()
{
    // ...
}

// MSTest: Use [TestCategory]
[TestCategory("Integration")]
[TestMethod]
public async Task Database_MigratesSuccessfully()
{
    // ...
}
```

```bash
# Run only unit tests (exclude integration):
dotnet test --filter "Category!=Integration"

# Run only integration tests (on schedule):
dotnet test --filter "Category=Integration"
```

---

## Test Infrastructure Checklist

Use this checklist when reviewing C# test code.

### Test Correctness
- [ ] All assertions target the **result** of the operation, not the setup/input values
- [ ] All async test methods return `Task` (not `void`)
- [ ] Every test method has at least one meaningful assertion
- [ ] Floating-point assertions use tolerance/precision parameter
- [ ] No shared mutable state between tests (no `static` mutable fields)
- [ ] `[Theory]` / `[TestCase]` data covers edge cases (null, empty, zero, boundary values)
- [ ] Mock setups match actual production call patterns (argument values, types)
- [ ] Exception assertions use `Assert.Throws<TException>`, not try-catch-Exception

### Test Reliability
- [ ] No dependency on `DateTime.Now` / `DateTimeOffset.Now` (use `TimeProvider`)
- [ ] No real network / external service calls in unit tests
- [ ] No `Thread.Sleep` for synchronization (use async wait with timeout)
- [ ] File paths use `Path.Combine` and relative paths from test context
- [ ] Shared fixtures reset state between tests or use per-test instances
- [ ] UI automation uses stable identifiers (AutomationId), not hard-coded names

### Test Design
- [ ] Tests verify observable behavior, not implementation details
- [ ] Mocks are for dependencies, not the system under test
- [ ] Complex test setup extracted into helper methods
- [ ] Test fixtures implement `IDisposable` / `IAsyncLifetime` when holding resources
- [ ] All test methods are `public` (test framework can discover them)
- [ ] Integration tests separated from unit tests (by project or trait/category)

## References

1. xUnit documentation — https://xunit.net/docs/getting-started/
2. NUnit documentation — https://docs.nunit.org/
3. MSTest documentation — Microsoft Learn: "MSTest overview"
4. Moq Quickstart — https://github.com/moq/moq4/wiki/Quickstart
5. .NET testing best practices — Microsoft Learn: "Unit testing best practices with .NET"
6. TimeProvider (.NET 8) — Microsoft Learn: "TimeProvider class"
7. FluentAssertions — https://fluentassertions.com/
