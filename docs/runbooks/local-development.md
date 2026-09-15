# Local Development Runbook

Start infrastructure with `docker compose up -d`, verify with `docker compose ps`, then run the template API with `dotnet run --project src/Services/Payments.Service.Template/Payments.Service.Template.Api`.

Use `/health/live` for process liveness and `/health/ready` for dependency readiness.