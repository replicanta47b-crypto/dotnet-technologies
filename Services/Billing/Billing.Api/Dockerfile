# Збірка з КОРЕНЯ репозиторію:
#   docker build -f Services/Billing/Billing.Api/Dockerfile -t billing-api .
#   docker run -p 8080:8080 -e ConnectionStrings__BillingDb="Server=host.docker.internal,1433;Database=BillingEfDb;User Id=sa;Password=...;TrustServerCertificate=true" billing-api
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Services/Billing/Billing.Domain/Billing.Domain.csproj Services/Billing/Billing.Domain/
COPY Services/Billing/Billing.Dal/Billing.Dal.csproj Services/Billing/Billing.Dal/
COPY Services/Billing/Billing.Bll/Billing.Bll.csproj Services/Billing/Billing.Bll/
COPY Services/Billing/Billing.Api/Billing.Api.csproj Services/Billing/Billing.Api/
RUN dotnet restore Services/Billing/Billing.Api/Billing.Api.csproj
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Services/Billing/ Services/Billing/
RUN dotnet publish Services/Billing/Billing.Api/Billing.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Billing.Api.dll"]
