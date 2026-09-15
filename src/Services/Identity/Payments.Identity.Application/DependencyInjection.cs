using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.Identity.Application.Contracts;
using Payments.Identity.Application.Security;

namespace Payments.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<RegisterUserRequest>, RegisterUserRequestValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IValidator<RefreshTokenRequest>, RefreshTokenRequestValidator>();
        return services;
    }
}
