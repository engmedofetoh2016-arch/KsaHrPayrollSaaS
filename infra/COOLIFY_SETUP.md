# Coolify deploy order

1) Provision database service (PostgreSQL) and object storage if needed (MinIO).
2) Deploy API service.
3) Run database migrations once.
4) Deploy Worker service.
5) Deploy Web service.

# API service (Dockerfile)
- Build context: repo root
- Dockerfile: src/backend/HrPayroll.Api/Dockerfile
- Internal port: 8080
- Health check path: /health
- Env source: infra/env.api.coolify.example

# Worker service (Dockerfile)
- Build context: repo root
- Dockerfile: src/backend/HrPayroll.Worker/Dockerfile
- Env source: infra/env.worker.coolify.example

# Web service (Dockerfile)
- Build context: repo root
- Dockerfile: src/web/Dockerfile
- Internal port: 80
- Health check path: /health
- Env source: infra/env.web.coolify.example

# Migration command (run once)
# Use API container/console:
# dotnet ef database update --project src/backend/HrPayroll.Infrastructure --startup-project src/backend/HrPayroll.Api
