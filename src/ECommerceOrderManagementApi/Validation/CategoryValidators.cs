using ECommerceOrderManagementApi.DTOs.Categories;
using FluentValidation;

namespace ECommerceOrderManagementApi.Validation;

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x))
            .WithMessage("Name must not be empty.").MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x))
            .WithMessage("Name must not be empty.").MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
