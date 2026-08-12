using System.ComponentModel.DataAnnotations;

namespace ECommerceOrderManagementApi.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required, MinLength(32)]
    public string Key { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int AccessTokenExpirationMinutes { get; init; } = 15;

    [Range(1, int.MaxValue)]
    public int RefreshTokenExpirationDays { get; init; } = 7;
}
