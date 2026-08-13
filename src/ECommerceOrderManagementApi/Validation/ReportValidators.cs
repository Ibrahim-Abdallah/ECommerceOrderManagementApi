using ECommerceOrderManagementApi.DTOs.Reports;
using FluentValidation;

namespace ECommerceOrderManagementApi.Validation;

public sealed class SalesSummaryQueryParametersValidator : AbstractValidator<SalesSummaryQueryParameters>
{
    public SalesSummaryQueryParametersValidator()
    {
        RuleFor(x => x)
            .Must(x => !x.FromDate.HasValue || !x.ToDate.HasValue || x.FromDate <= x.ToDate)
            .WithMessage("FromDate must not be later than ToDate.");
    }
}
