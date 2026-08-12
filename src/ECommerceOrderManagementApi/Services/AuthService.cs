using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class AuthService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return RegistrationResult.DuplicateEmail();
        }

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = UserRole.Customer,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;

            if (await dbContext.Users.AnyAsync(
                    existingUser => existingUser.NormalizedEmail == normalizedEmail,
                    cancellationToken))
            {
                return RegistrationResult.DuplicateEmail();
            }

            throw;
        }

        return RegistrationResult.Success(new RegisterResponse(
            user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.CreatedAtUtc));
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        return await IssueTokenPairAsync(user, cancellationToken);
    }

    public async Task<AuthResponse?> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (storedToken is null || storedToken.RevokedAtUtc is not null || storedToken.ExpiresAtUtc <= now)
        {
            return null;
        }

        var accessToken = tokenService.CreateAccessToken(storedToken.User);
        var generatedRefreshToken = tokenService.CreateRefreshToken();
        var replacement = CreateRefreshTokenEntity(storedToken.User, generatedRefreshToken, now);

        storedToken.RevokedAtUtc = now;
        storedToken.ReplacedByToken = replacement;
        dbContext.RefreshTokens.Add(replacement);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        return new AuthResponse(accessToken.Token, generatedRefreshToken.Token, accessToken.ExpiresAtUtc);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (storedToken is null || storedToken.RevokedAtUtc is not null || storedToken.ExpiresAtUtc <= now)
        {
            return;
        }

        storedToken.RevokedAtUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request already revoked or rotated this token.
        }
    }

    internal static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private async Task<AuthResponse> IssueTokenPairAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var accessToken = tokenService.CreateAccessToken(user);
        var generatedRefreshToken = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(CreateRefreshTokenEntity(user, generatedRefreshToken, now));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(accessToken.Token, generatedRefreshToken.Token, accessToken.ExpiresAtUtc);
    }

    private static RefreshToken CreateRefreshTokenEntity(
        User user,
        GeneratedRefreshToken generatedToken,
        DateTime createdAtUtc) => new()
    {
        User = user,
        UserId = user.Id,
        TokenHash = generatedToken.TokenHash,
        CreatedAtUtc = createdAtUtc,
        ExpiresAtUtc = generatedToken.ExpiresAtUtc
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
