# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KsaHrPayrollSaaS.sln ./
COPY src/backend/HrPayroll.Domain/HrPayroll.Domain.csproj src/backend/HrPayroll.Domain/
COPY src/backend/HrPayroll.Application/HrPayroll.Application.csproj src/backend/HrPayroll.Application/
COPY src/backend/HrPayroll.Infrastructure/HrPayroll.Infrastructure.csproj src/backend/HrPayroll.Infrastructure/
COPY src/backend/HrPayroll.Api/HrPayroll.Api.csproj src/backend/HrPayroll.Api/
RUN dotnet restore src/backend/HrPayroll.Api/HrPayroll.Api.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/HrPayroll.Api/HrPayroll.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "HrPayroll.Api.dll"]
