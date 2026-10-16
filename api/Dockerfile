# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS source

ENV DOTNET_CLI_HOME=/home/app \
    NUGET_PACKAGES=/home/app/.nuget/packages
WORKDIR /app
RUN mkdir -p bin obj "$NUGET_PACKAGES" \
    && chown -R app:app /app /home/app
USER $APP_UID
COPY --chown=app:app api.csproj ./
RUN dotnet restore api.csproj
COPY --chown=app:app . .

FROM source AS ef-tools
RUN dotnet tool restore
ENTRYPOINT ["dotnet", "ef"]

FROM source AS publish
RUN dotnet publish api.csproj --configuration Release --no-restore \
    --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=5s --start-period=10s --retries=10 \
    CMD curl --fail --silent --max-time 4 http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "api.dll"]
