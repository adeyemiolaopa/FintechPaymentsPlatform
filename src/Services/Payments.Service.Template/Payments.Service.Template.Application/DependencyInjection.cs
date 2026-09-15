using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Service.Template.Application.Examples;

namespace Payments.Service.Template.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTemplateApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateExampleRequest>, CreateExampleRequestValidator>();
        services.AddScoped<ICommandHandler<CreateExampleCommand, CreateExampleResponse>, CreateExampleCommandHandler>();
        services.AddScoped<IQueryHandler<GetExampleByIdQuery, ExampleResponse>, GetExampleByIdQueryHandler>();
        return services;
    }
}
