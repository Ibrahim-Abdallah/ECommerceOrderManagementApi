using ECommerceOrderManagementApi.DTOs.Auth;

namespace ECommerceOrderManagementApi.Interfaces;

public interface IAuthService
{
    Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public sealed record RegistrationResult(RegisterResponse? User, bool IsDuplicateEmail)
{
    public static RegistrationResult Success(RegisterResponse user) => new(user, false);
    public static RegistrationResult DuplicateEmail() => new(null, true);
}
