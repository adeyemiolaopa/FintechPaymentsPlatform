using FluentValidation;
using Payments.Identity.Application.Contracts;

namespace Payments.Identity.Application.Security;

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.PhoneNumber).NotEmpty().Matches("^\\+[1-9][0-9]{7,14}$").WithMessage("Phone number must be in E.164 format.");
        RuleFor(request => request.Password).SetValidator(new PasswordPolicyValidator());
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Password).NotEmpty();
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() => RuleFor(request => request.RefreshToken).NotEmpty().MinimumLength(32);
}

public sealed class PasswordPolicyValidator : AbstractValidator<string>
{
    public PasswordPolicyValidator()
    {
        RuleFor(password => password)
            .NotEmpty()
            .MinimumLength(12)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
    }
}
