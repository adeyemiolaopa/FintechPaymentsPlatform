using FluentValidation;

namespace Payments.Service.Template.Application.Examples;

public sealed class CreateExampleRequestValidator : AbstractValidator<CreateExampleRequest>
{
    public CreateExampleRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(request => request.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();

        RuleFor(request => request.ExternalReference)
            .NotEmpty()
            .Length(8, 64)
            .Matches("^[a-zA-Z0-9_-]+$");
    }
}
