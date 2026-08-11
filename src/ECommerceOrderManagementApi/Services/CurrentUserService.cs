using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ECommerceOrderManagementApi.Interfaces;

namespace ECommerceOrderManagementApi.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId => int.TryParse(
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub),
        out var userId) ? userId : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);
}
