using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using ECommerceOrderManagementApi.DTOs.Categories;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Quality;

public sealed class ExceptionHandlingApiTests
{
    [Fact]
    public async Task UnexpectedException_ReturnsSafeProblemDetailsWithTraceId()
    {
        await using var factory = new ExceptionHandlingApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/categories");
        var body = await response.Content.ReadAsStringAsync();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem!.Status);
        Assert.True(problem.Extensions.TryGetValue("traceId", out var traceId));
        var responseTraceId = traceId?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(responseTraceId));
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated-internal-detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.GetCurrentDirectory(), body, StringComparison.OrdinalIgnoreCase);

        var log = Assert.Single(factory.Logs.Entries, entry =>
            entry.Category == typeof(ECommerceOrderManagementApi.Errors.UnexpectedExceptionHandler).FullName);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Contains(nameof(InvalidOperationException), log.Message, StringComparison.Ordinal);
        Assert.True(log.Properties.TryGetValue("TraceId", out var loggedTraceId));
        Assert.False(string.IsNullOrWhiteSpace(loggedTraceId?.ToString()));
        Assert.Equal(typeof(InvalidOperationException).FullName, log.Properties["ExceptionType"]);
        Assert.DoesNotContain("simulated-internal-detail", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=private", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\internal\\source.cs", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_MarksProtectedOperationsButKeepsAnonymousOperationsOpen()
    {
        await using var factory = new ExceptionHandlingApiFactory();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        Assert.Equal("Bearer", root.GetProperty("paths").GetProperty("/api/cart").GetProperty("get")
            .GetProperty("security")[0].EnumerateObject().Single().Name);
        Assert.False(root.GetProperty("paths").GetProperty("/api/auth/login").GetProperty("post")
            .TryGetProperty("security", out _));
        Assert.False(root.GetProperty("paths").GetProperty("/api/products").GetProperty("get")
            .TryGetProperty("security", out _));
    }
}

public sealed class ExceptionHandlingApiFactory : WebApplicationFactory<Program>
{
    public TestLogCollector Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ECommerceOrderManagementApi",
                ["Jwt:Audience"] = "ECommerceOrderManagementApi.Client",
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long"
            }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(new TestLoggerProvider(Logs));
            services.RemoveAll<ICategoryService>();
            services.AddScoped<ICategoryService, ThrowingCategoryService>();
        });
    }
}

public sealed class TestLogCollector
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();

    public IReadOnlyCollection<TestLogEntry> Entries => entries.ToArray();

    public void Add(TestLogEntry entry) => entries.Enqueue(entry);
}

public sealed record TestLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

internal sealed class TestLoggerProvider(TestLogCollector collector) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, collector);

    public void Dispose()
    {
    }
}

internal sealed class TestLogger(string category, TestLogCollector collector) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception is not null)
            message = $"{message}{Environment.NewLine}{exception}";

        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.Where(value => value.Key != "{OriginalFormat}")
                .ToDictionary(value => value.Key, value => value.Value)
            : new Dictionary<string, object?>();
        collector.Add(new TestLogEntry(category, logLevel, message, properties));
    }
}

internal sealed class ThrowingCategoryService : ICategoryService
{
    public Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("simulated-internal-detail Server=private; C:\\internal\\source.cs");

    public Task<CategoryResponse?> GetAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CategoryWriteResult> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CategoryWriteResult> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CategoryWriteStatus> DeactivateAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
}
