using ECommerceOrderManagementApi.DTOs.Auth;
using FluentValidation;

namespace ECommerceOrderManagementApi.Validation;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty();
    }
}
