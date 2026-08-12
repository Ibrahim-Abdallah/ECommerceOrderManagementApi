using ECommerceOrderManagementApi.DTOs.Auth;
using FluentValidation;

namespace ECommerceOrderManagementApi.Validation;

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(request => request.RefreshToken)
            .NotEmpty()
            .Must(token => !string.IsNullOrWhiteSpace(token))
            .WithMessage("Refresh token must not be whitespace.")
            .MaximumLength(512);
    }
}
