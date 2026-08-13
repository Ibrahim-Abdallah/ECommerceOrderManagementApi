using ECommerceOrderManagementApi.DTOs.Orders;
using FluentValidation;

namespace ECommerceOrderManagementApi.Validation;

public sealed class CustomerOrderQueryParametersValidator : AbstractValidator<CustomerOrderQueryParameters>
{
    public CustomerOrderQueryParametersValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x).Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
            .WithMessage("FromDate must not be later than ToDate.");
    }
}

public sealed class AdminOrderQueryParametersValidator : AbstractValidator<AdminOrderQueryParameters>
{
    public AdminOrderQueryParametersValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.CustomerEmail).MaximumLength(256);
        RuleFor(x => x).Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
            .WithMessage("FromDate must not be later than ToDate.");
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}
