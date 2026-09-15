using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.Customer.Application.Customers;

namespace Payments.Customer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<UpdateCustomerProfileRequest>, UpdateCustomerProfileRequestValidator>();
        return services;
    }
}
