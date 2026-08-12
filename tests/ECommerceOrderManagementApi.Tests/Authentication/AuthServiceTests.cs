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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));
        Assert.Equal(Now.UtcDateTime.AddMinutes(15), response.ExpiresAtUtc);
        var principal = Validate(response.AccessToken);
        Assert.Equal("1", principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal("ada@example.com", principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal("Customer", principal.FindFirstValue(ClaimTypes.Role));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));

        var storedToken = Assert.Single(context.RefreshTokens);
        Assert.NotEqual(response.RefreshToken, storedToken.TokenHash);
        Assert.Equal(CreateTokenService().HashRefreshToken(response.RefreshToken), storedToken.TokenHash);
        Assert.Equal(1, storedToken.UserId);
        Assert.Equal(Now.UtcDateTime, storedToken.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddDays(7), storedToken.ExpiresAtUtc);
        Assert.Null(storedToken.RevokedAtUtc);
    }

    [Fact]
    public void RefreshTokenGeneration_IsSecureUniqueHashedAndUsesConfiguredExpiry()
    {
        var tokenService = CreateTokenService();

        var first = tokenService.CreateRefreshToken();
        var second = tokenService.CreateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(first.Token));
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.TokenHash, second.TokenHash);
        Assert.Equal(first.TokenHash, tokenService.HashRefreshToken(first.Token));
        Assert.Equal(first.TokenHash, tokenService.HashRefreshToken(first.Token));
        Assert.Equal(Now.UtcDateTime.AddDays(7), first.ExpiresAtUtc);
    }

    [Fact]
    public async Task RefreshToken_ValidTokenRotatesAndReplayIsRejected()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);
        var login = await service.LoginAsync(new LoginRequest("ada@example.com", "Strong1!"), default);

        var refreshed = await service.RefreshTokenAsync(new RefreshTokenRequest(login!.RefreshToken), default);

        Assert.NotNull(refreshed);
        Assert.NotEqual(login.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        var tokens = await context.RefreshTokens.OrderBy(token => token.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(Now.UtcDateTime, tokens[0].RevokedAtUtc);
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedByTokenId);
        Assert.Null(tokens[1].RevokedAtUtc);
        Assert.Equal(tokens[0].UserId, tokens[1].UserId);
        Assert.Equal(Now.UtcDateTime.AddDays(7), tokens[1].ExpiresAtUtc);

        var replay = await service.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken), default);
        Assert.Null(replay);
        Assert.Equal(2, await context.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task RefreshToken_UnknownRevokedAndExpiredTokensAreRejected()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);
        var login = await service.LoginAsync(new LoginRequest("ada@example.com", "Strong1!"), default);
        var token = Assert.Single(context.RefreshTokens);

        Assert.Null(await service.RefreshTokenAsync(new RefreshTokenRequest("unknown"), default));
        token.RevokedAtUtc = Now.UtcDateTime;
        await context.SaveChangesAsync();
        Assert.Null(await service.RefreshTokenAsync(new RefreshTokenRequest(login!.RefreshToken), default));
        token.RevokedAtUtc = null;
        token.ExpiresAtUtc = Now.UtcDateTime;
        await context.SaveChangesAsync();
        Assert.Null(await service.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken), default));
    }

    [Fact]
    public async Task Logout_RevokesOnlyActiveTokenAndIsIdempotent()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);
        var login = await service.LoginAsync(new LoginRequest("ada@example.com", "Strong1!"), default);
        var request = new RefreshTokenRequest(login!.RefreshToken);

        await service.LogoutAsync(request, default);
        await service.LogoutAsync(request, default);
        await service.LogoutAsync(new RefreshTokenRequest("unknown"), default);

        Assert.Equal(Now.UtcDateTime, Assert.Single(context.RefreshTokens).RevokedAtUtc);
        Assert.Null(await service.RefreshTokenAsync(request, default));
    }

    [Fact]
    public async Task Logout_ExpiredTokenRemainsUnchanged()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);
        var login = await service.LoginAsync(new LoginRequest("ada@example.com", "Strong1!"), default);
        var token = Assert.Single(context.RefreshTokens);
        token.ExpiresAtUtc = Now.UtcDateTime;
        await context.SaveChangesAsync();

        await service.LogoutAsync(new RefreshTokenRequest(login!.RefreshToken), default);

        Assert.Null(token.RevokedAtUtc);
    }

    [Fact]
    public async Task Logout_ConcurrencyConflictIsTreatedAsIdempotentSuccess()
    {
        var interceptor = new LogoutConcurrencyInterceptor();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(interceptor)
                .Options);
        var service = CreateService(context);
        await service.RegisterAsync(
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Strong1!", "Strong1!"), default);
        var login = await service.LoginAsync(new LoginRequest("ada@example.com", "Strong1!"), default);
        interceptor.ThrowOnSave = true;

        var exception = await Record.ExceptionAsync(() =>
            service.LogoutAsync(new RefreshTokenRequest(login!.RefreshToken), default));

        Assert.Null(exception);
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
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });
        var tokenService = new TokenService(options, timeProvider);
        return new AuthService(context, new PasswordHasher<User>(), tokenService, timeProvider);
    }

    private static TokenService CreateTokenService()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ECommerceOrderManagementApi",
            Audience = "ECommerceOrderManagementApi.Client",
            Key = Key,
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });
        return new TokenService(options, new FixedTimeProvider(Now));
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

    private sealed class LogoutConcurrencyInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new DbUpdateConcurrencyException("Simulated competing refresh-token revocation.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
