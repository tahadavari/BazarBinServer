FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BazarBin.Mcp.Client/BazarBin.Mcp.Client.csproj", "BazarBin.Mcp.Client/"]
RUN dotnet restore "BazarBin.Mcp.Client/BazarBin.Mcp.Client.csproj"
COPY . .
WORKDIR "/src/BazarBin.Mcp.Client"
RUN dotnet build "BazarBin.Mcp.Client.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BazarBin.Mcp.Client.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BazarBin.Mcp.Client.dll"]
