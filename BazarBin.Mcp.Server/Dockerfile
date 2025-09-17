FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BazarBin.Mcp.Server/BazarBin.Mcp.Server.csproj", "BazarBin.Mcp.Server/"]
RUN dotnet restore "BazarBin.Mcp.Server/BazarBin.Mcp.Server.csproj"
COPY . .
WORKDIR "/src/BazarBin.Mcp.Server"
RUN dotnet build "BazarBin.Mcp.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BazarBin.Mcp.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BazarBin.Mcp.Server.dll"]
