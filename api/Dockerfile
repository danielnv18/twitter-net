FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /app
COPY . .
EXPOSE 8080

# Keep the initial skeleton runnable without generating a .NET project.
CMD ["sh", "-c", "if ls ./*.csproj >/dev/null 2>&1; then exec dotnet run --no-launch-profile --urls http://0.0.0.0:8080; else echo 'API placeholder: add a .NET project in api/, then restart this service.'; exec sleep infinity; fi"]
