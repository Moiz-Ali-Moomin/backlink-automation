# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src
COPY . .
RUN dotnet restore BacklinkStudio.sln
RUN mkdir -p /out/reports \
    && dotnet publish src/BacklinkStudio.Api/BacklinkStudio.Api.csproj -c Release --no-restore -o /out/api \
    && dotnet publish src/BacklinkStudio.Mcp/BacklinkStudio.Mcp.csproj -c Release --no-restore -o /out/mcp \
    && dotnet publish src/BacklinkStudio.Worker/BacklinkStudio.Worker.csproj -c Release --no-restore -o /out/worker \
    && dotnet publish src/BacklinkStudio.Cli/BacklinkStudio.Cli.csproj -c Release --no-restore -o /out/cli

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /out/api ./api
COPY --from=build --chown=$APP_UID:$APP_UID /out/mcp ./mcp
COPY --from=build --chown=$APP_UID:$APP_UID /out/worker ./worker
COPY --from=build --chown=$APP_UID:$APP_UID /out/cli ./cli
COPY --from=build --chown=$APP_UID:$APP_UID /out/reports /var/lib/backlinkstudio/reports
USER $APP_UID

FROM mcr.microsoft.com/playwright/dotnet:v1.62.0-noble AS worker-runtime
WORKDIR /app
COPY --from=build --chown=pwuser:pwuser /out/worker ./worker
COPY --from=build --chown=pwuser:pwuser /out/reports /var/lib/backlinkstudio/reports
USER pwuser
ENTRYPOINT ["dotnet"]

FROM runtime AS final
