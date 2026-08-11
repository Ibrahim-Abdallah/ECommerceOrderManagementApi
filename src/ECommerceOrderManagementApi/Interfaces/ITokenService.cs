using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.Entities;

namespace ECommerceOrderManagementApi.Interfaces;

public interface ITokenService
{
    AuthResponse CreateAccessToken(User user);
}
