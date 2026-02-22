# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KsaHrPayrollSaaS.sln ./
COPY src/backend/HrPayroll.Domain/HrPayroll.Domain.csproj src/backend/HrPayroll.Domain/
COPY src/backend/HrPayroll.Application/HrPayroll.Application.csproj src/backend/HrPayroll.Application/
COPY src/backend/HrPayroll.Infrastructure/HrPayroll.Infrastructure.csproj src/backend/HrPayroll.Infrastructure/
COPY src/backend/HrPayroll.Worker/HrPayroll.Worker.csproj src/backend/HrPayroll.Worker/
RUN dotnet restore src/backend/HrPayroll.Worker/HrPayroll.Worker.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/HrPayroll.Worker/HrPayroll.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

# ✅ IMPORTANT: aspnet (includes Microsoft.AspNetCore.App)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "HrPayroll.Worker.dll"]