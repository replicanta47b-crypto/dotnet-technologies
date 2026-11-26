# billing-api (у завданні лаб. 10 — «catalog-api»)
# Збірка з КОРЕНЯ репозиторію:
#   docker build -t billing-api:v1 -f Services/Billing/Billing.Api/Dockerfile .
# Етап 1 (SDK ~800 МБ) лише компілює; етап 2 (aspnet ~200 МБ) отримує тільки результат публікації.
# Спершу копіюються лише .csproj і виконується restore — цей шар кешується, тож зміна коду не завантажує NuGet заново.

# ---- Етап 1: restore (кешований шар) і збірка ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ["Platform.ServiceDefaults/Platform.ServiceDefaults.csproj", "Platform.ServiceDefaults/"]
COPY ["Platform.EventBus/Platform.EventBus.csproj", "Platform.EventBus/"]
COPY ["Platform.Security/Platform.Security.csproj", "Platform.Security/"]
COPY ["Services/Billing/Billing.Domain/Billing.Domain.csproj", "Services/Billing/Billing.Domain/"]
COPY ["Services/Billing/Billing.Dal/Billing.Dal.csproj", "Services/Billing/Billing.Dal/"]
COPY ["Services/Billing/Billing.Bll/Billing.Bll.csproj", "Services/Billing/Billing.Bll/"]
COPY ["Services/Billing/Billing.Api/Billing.Api.csproj", "Services/Billing/Billing.Api/"]
RUN dotnet restore "Services/Billing/Billing.Api/Billing.Api.csproj"
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.EventBus/ Platform.EventBus/
COPY Platform.Security/ Platform.Security/
COPY Services/Billing/ Services/Billing/
WORKDIR "/src/Services/Billing/Billing.Api"
RUN dotnet build "Billing.Api.csproj" -c Release --no-restore -o /app/build

# ---- Етап 2: публікація ----
FROM build AS publish
RUN dotnet publish "Billing.Api.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- Етап 3: фінальний образ ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
# .NET 8+ образи слухають 8080 і працюють від non-root користувача (APP_UID); порт задаємо явно
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Billing.Api.dll"]
