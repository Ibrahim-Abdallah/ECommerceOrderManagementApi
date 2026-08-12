using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [EndpointSummary("Register a customer account")]
    public async Task<ActionResult<RegisterResponse>> Register(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var result = await authService.RegisterAsync(request, cancellationToken);
        if (result.IsDuplicateEmail)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email already registered",
                Detail = "An account with this email already exists."
            });
        }

        return StatusCode(StatusCodes.Status201Created, result.User);
    }

    [HttpPost("login")]
    [EndpointSummary("Log in and receive an access/refresh token pair")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid credentials",
                Detail = "The supplied email or password is invalid."
            })
            : Ok(response);
    }

    [HttpPost("refresh-token")]
    [EndpointSummary("Rotate a refresh token and receive a new token pair")]
    public async Task<ActionResult<AuthResponse>> RefreshToken(
        RefreshTokenRequest request,
        IValidator<RefreshTokenRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var response = await authService.RefreshTokenAsync(request, cancellationToken);
        return response is null ? InvalidRefreshToken() : Ok(response);
    }

    [HttpPost("logout")]
    [EndpointSummary("Revoke a refresh token")]
    public async Task<IActionResult> Logout(
        RefreshTokenRequest request,
        IValidator<RefreshTokenRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        }

        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    private UnauthorizedObjectResult InvalidRefreshToken() => Unauthorized(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Invalid refresh token",
        Detail = "The supplied refresh token is invalid."
    });
}
