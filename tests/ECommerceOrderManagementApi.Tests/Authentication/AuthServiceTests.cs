using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ECommerceOrderManagementApi.Configuration;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Authentication;

public sealed class AuthServiceTests
{
    private const string Key = "phase-two-test-signing-key-at-least-32-bytes-long";
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_PersistsNormalizedCustomerWithHashedPassword()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RegisterAsync(
            new RegisterRequest("  Ada ", " Lovelace  ", " Ada@Example.com ", "Strong1!", "Strong1!"), default);

        var user = Assert.Single(context.Users);
        Assert.False(result.IsDuplicateEmail);
        Assert.Equal("Ada", user.FirstName);
        Assert.Equal("Lovelace", user.LastName);
        Assert.Equal("Ada@Example.com", user.Email);
        Assert.Equal("ADA@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.NotEqual("Strong1!", user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, "Strong1!"));
        Assert.Equal(Now.UtcDateTime, user.CreatedAtUtc);
        Assert.Equal(UserRole.Customer, result.User!.Role);
        Assert.DoesNotContain("PasswordHash", System.Text.Json.JsonSerializer.Serialize(result.User));
    }

    [Fact]
    public async Task Register_RejectsDuplicateEmailIgnoringCase()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var first = new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!");
        var second = first with { Email = " ADA@EXAMPLE.COM " };

        await service.RegisterAsync(first, default);
        var result = await service.RegisterAsync(second, default);

        Assert.True(result.IsDuplicateEmail);
        Assert.Single(context.Users);
    }

    [Fact]
    public async Task Login_ValidCredentialsIssueValidExpectedJwt()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);

        var response = await service.LoginAsync(new LoginRequest(" ADA@example.COM ", "Strong1!"), default);

        Assert.NotNull(response);
        Assert.Equal(Now.UtcDateTime.AddMinutes(15), response.ExpiresAtUtc);
        var principal = Validate(response.AccessToken);
        Assert.Equal("1", principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal("ada@example.com", principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal("Customer", principal.FindFirstValue(ClaimTypes.Role));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPasswordHaveEquivalentFailure()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);

        var unknown = await service.LoginAsync(new LoginRequest("unknown@example.com", "Strong1!"), default);
        var wrong = await service.LoginAsync(new LoginRequest("ada@example.com", "Wrong1!"), default);

        Assert.Null(unknown);
        Assert.Null(wrong);
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AuthService CreateService(AppDbContext context)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ECommerceOrderManagementApi",
            Audience = "ECommerceOrderManagementApi.Client",
            Key = Key,
            AccessTokenExpirationMinutes = 15
        });
        var tokenService = new TokenService(options, timeProvider);
        return new AuthService(context, new PasswordHasher<User>(), tokenService, timeProvider);
    }

    private static ClaimsPrincipal Validate(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ECommerceOrderManagementApi",
            ValidateAudience = true,
            ValidAudience = "ECommerceOrderManagementApi.Client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            ValidateLifetime = false,
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        }, out _);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
