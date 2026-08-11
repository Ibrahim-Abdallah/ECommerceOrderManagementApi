namespace ECommerceOrderManagementApi.Entities;

public sealed class RefreshToken
{
    public int Id { get; set; }
    public required string TokenHash { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int? ReplacedByTokenId { get; set; }
    public required User User { get; set; }
    public RefreshToken? ReplacedByToken { get; set; }
}
