# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY FintechPaymentsPlatform.sln ./
COPY src ./src
COPY tests ./tests
RUN dotnet restore FintechPaymentsPlatform.sln

FROM restore AS build
RUN dotnet publish src/Services/Payments.Service.Template/Payments.Service.Template.Api/Payments.Service.Template.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
LABEL org.opencontainers.image.title="payments-template-api" \
      org.opencontainers.image.description="Template API for fintech payments platform foundation" \
      org.opencontainers.image.source="FintechPaymentsPlatform"
WORKDIR /app
RUN addgroup --system payments && adduser --system --ingroup payments payments && apk add --no-cache curl
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER payments
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl -f http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Payments.Service.Template.Api.dll"]

