using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Authentication;

public sealed class AuthenticationApiTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly HttpClient _client;

    public AuthenticationApiTests(AuthenticationApiFactory factory) => _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task RegisterLoginAndBearerAuthentication_WorkEndToEnd()
    {
        var request = new RegisterRequest("Ada", "Lovelace", $"ada-{Guid.NewGuid()}@example.com", "Strong1!", "Strong1!");

        var registration = await _client.PostAsJsonAsync("/api/auth/register", request);
        var duplicate = await _client.PostAsJsonAsync("/api/auth/register", request with { Email = request.Email.ToUpperInvariant() });
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, request.Password));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        var anonymousProtected = await _client.GetAsync("/test/protected");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var authenticatedProtected = await _client.GetAsync("/test/protected");

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousProtected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticatedProtected.StatusCode);
    }

    [Fact]
    public async Task RefreshRotationReplayLogoutAndValidationFollowHttpContract()
    {
        var email = $"refresh-{Guid.NewGuid()}@example.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Ada", "Lovelace", email, "Strong1!", "Strong1!"));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Strong1!"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token", new RefreshTokenRequest(login!.RefreshToken));
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        var replay = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token", new RefreshTokenRequest(login.RefreshToken));
        var logout = await _client.PostAsJsonAsync(
            "/api/auth/logout", new RefreshTokenRequest(refreshed!.RefreshToken));
        var repeatedLogout = await _client.PostAsJsonAsync(
            "/api/auth/logout", new RefreshTokenRequest(refreshed.RefreshToken));
        var unknownLogout = await _client.PostAsJsonAsync(
            "/api/auth/logout", new RefreshTokenRequest("unknown"));
        var loggedOutRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token", new RefreshTokenRequest(refreshed.RefreshToken));
        var invalidRequest = await _client.PostAsJsonAsync(
            "/api/auth/refresh-token", new RefreshTokenRequest("   "));

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknownLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOutRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRequest.StatusCode);
    }

    [Fact]
    public async Task InvalidRegistrationReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("", "", "invalid", "weak", "different"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownEmailAndWrongPasswordReturnEquivalentUnauthorizedResponses()
    {
        var email = $"login-{Guid.NewGuid()}@example.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Ada", "Lovelace", email, "Strong1!", "Strong1!"));

        var unknown = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("unknown@example.com", "Strong1!"));
        var wrong = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidBearerTokenIsRejected()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");

        var response = await _client.GetAsync("/test/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredBearerTokenIsRejected()
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "ECommerceOrderManagementApi",
            audience: "ECommerceOrderManagementApi.Client",
            notBefore: now.AddMinutes(-2),
            expires: now.AddMinutes(-1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-at-least-32-bytes-long")),
                SecurityAlgorithms.HmacSha256));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await _client.GetAsync("/test/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ECommerceOrderManagementApi",
                ["Jwt:Audience"] = "ECommerceOrderManagementApi.Client",
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            }));
        builder.ConfigureServices(services =>
        {
            var databaseName = $"AuthApiTests-{Guid.NewGuid()}";
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.AddControllers().AddApplicationPart(typeof(TestProtectedController).Assembly);
        });
    }
}

[ApiController]
[Route("test/protected")]
public sealed class TestProtectedController : ControllerBase
{
    [Authorize]
    [HttpGet]
    public IActionResult Get() => Ok(new { authenticated = true });
}
