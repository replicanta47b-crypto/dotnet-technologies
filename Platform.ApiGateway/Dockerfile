# gateway (YARP)
# Збірка з КОРЕНЯ репозиторію:
#   docker build -t gateway:v1 -f Platform.ApiGateway/Dockerfile .
# Етап 1 (SDK ~800 МБ) лише компілює; етап 2 (aspnet ~200 МБ) отримує тільки результат публікації.
# Спершу копіюються лише .csproj і виконується restore — цей шар кешується, тож зміна коду не завантажує NuGet заново.

# ---- Етап 1: restore (кешований шар) і збірка ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY ["Platform.ServiceDefaults/Platform.ServiceDefaults.csproj", "Platform.ServiceDefaults/"]
COPY ["Platform.Security/Platform.Security.csproj", "Platform.Security/"]
COPY ["Platform.ApiGateway/Platform.ApiGateway.csproj", "Platform.ApiGateway/"]
RUN dotnet restore "Platform.ApiGateway/Platform.ApiGateway.csproj"
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.Security/ Platform.Security/
COPY Platform.ApiGateway/ Platform.ApiGateway/
WORKDIR "/src/Platform.ApiGateway"
RUN dotnet build "Platform.ApiGateway.csproj" -c Release --no-restore -o /app/build

# ---- Етап 2: публікація ----
FROM build AS publish
RUN dotnet publish "Platform.ApiGateway.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- Етап 3: фінальний образ ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
# .NET 8+ образи слухають 8080 і працюють від non-root користувача (APP_UID); порт задаємо явно
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Platform.ApiGateway.dll"]
