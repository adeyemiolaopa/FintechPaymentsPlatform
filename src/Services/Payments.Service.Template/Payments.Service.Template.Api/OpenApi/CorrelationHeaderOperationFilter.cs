using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Payments.Service.Template.Api.OpenApi;

public sealed class CorrelationHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Correlation-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional request correlation identifier. Generated when omitted.",
        });
    }
}
