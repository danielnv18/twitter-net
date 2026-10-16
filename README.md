# Twitter Net

A basic X/Twitter clone planned with a .NET API, a Vue.js client, and MySQL. Use Docker Compose for the production stack and host CLI commands for development. No X/Twitter application features are implemented yet.

## Structure

```text
api/                 # .NET 10 API and multi-stage Dockerfile
client/              # Vue.js client, multi-stage Dockerfile, and Nginx config
docker-compose.yml   # Built API, compiled client, and MySQL services
docker-compose.dev.yml # Development MySQL for host CLI commands
scripts/             # HTTP smoke tests for the running apps
.env.example         # Optional production Compose configuration
LICENSE              # MIT license
```

## Run the production stack with Docker

Install Docker with Docker Compose, then run from the repository root:

```sh
docker compose up --build --wait
```

Docker publishes the API in Release mode and runs its DLL in an ASP.NET runtime image. The client build runs `npm ci` and `npm run build`, including TypeScript checking, then copies only `dist/` into an Nginx runtime image. Both apps run their compiled output; there are no source mounts, development servers, file watchers, or hot reload.

The command waits until all three services are healthy:

- Client: <http://localhost:5173>
- API health: <http://localhost:8080/health>
- Database readiness: <http://localhost:8080/health/ready>

The API runs in `Production`, so its development OpenAPI route is unavailable. `GET /health` returns HTTP 200 with a plain-text `Healthy` body when the API is running. It is a liveness check; it does not test MySQL connectivity. `GET /health/ready` returns HTTP 200 with `Healthy` when EF Core can connect to MySQL, or HTTP 503 with `Unhealthy` when it cannot. Readiness checks connectivity, not application schema. MySQL has its own Compose health check, and the API starts after MySQL is healthy.

Verify the running apps with:

```sh
sh scripts/smoke-test.sh
docker compose ps
```

The smoke test checks API health, database readiness, the client HTML and compiled JavaScript, Vue history route fallback, and missing static asset responses. For custom published ports, pass `API_PORT` and `CLIENT_PORT` to the script, or set `API_URL` and `CLIENT_URL` directly. For example: `API_PORT=8081 CLIENT_PORT=5174 sh scripts/smoke-test.sh`.

After changing either app or its dependencies, rebuild and recreate the containers with `docker compose up --build --wait`. Restarting an existing container does not rebuild the app. Commit client dependency changes with the updated `client/package-lock.json`.

## Run development with dotnet and npm

Install Make, Docker with Docker Compose, the .NET 10 SDK, and a Node.js version supported by `client/package.json`. Install dependencies, then start development from the repository root:

```sh
make install
make dev
```

`make install` installs the client npm dependencies, restores API NuGet packages, and restores the pinned EF CLI tool. `make dev` starts development MySQL and runs the API and client together in your terminal. Stop the production stack with `docker compose down` first if it is running. Press Ctrl+C to stop both apps. Development MySQL stays running. Stop it with `docker compose -f docker-compose.dev.yml down` before switching to production or running `make migrate`. Run `make help` to list the shortcuts.

To run each app separately, use the commands below. Docker runs only MySQL during development. Start the development database from the repository root:

```sh
docker compose -f docker-compose.dev.yml up -d --wait
```

Run the API from the repository root:

```sh
dotnet run --project api/api.csproj
```

You can also run `dotnet run` from `api/`. The launch profile selects `Development`, and `api/appsettings.Development.json` supplies the local connection string.

In another terminal, run the client:

```sh
cd client
npm install
npm run dev
```

The development URLs are:

- Client: <http://localhost:5173>
- API health: <http://localhost:5078/health>
- Database readiness: <http://localhost:5078/health/ready>
- OpenAPI: <http://localhost:5078/openapi/v1.json>

Development MySQL listens on `127.0.0.1:3307`. Its database, user, and password are all `twitter_net_dev`. These fixed local defaults match `api/appsettings.Development.json`. To use another database or credentials, update that file or override `ConnectionStrings__DefaultConnection` through an environment variable or .NET user secrets. The API does not load the root Compose `.env` file.

Stop the current mode before starting the other. To switch from the production stack to development, run from the repository root:

```sh
docker compose down
docker compose -f docker-compose.dev.yml up -d --wait
```

Then start the API and client with the commands above. To switch back, stop both CLI processes with Ctrl+C, then run:

```sh
docker compose -f docker-compose.dev.yml down
docker compose up --build --wait
```

Each mode keeps its own database volume. Development uses `twitter-net-dev_mysql_data`; the production stack uses `twitter-net_mysql_data`. Both `down` commands preserve data.

## Container configuration

Both app containers run as non-root users with dropped Linux capabilities and privilege escalation disabled. Their root filesystems are read-only, with writable temporary storage in `/tmp`. Runtime images omit the .NET SDK, Node.js, source code, and development dependencies. Each app image includes a health check, including when run without Compose.

Nginx supports Vue history routes, caches hashed assets, and revalidates the HTML entry point. Container logs rotate at 10 MB per file with three files per service. Restart policies restart stopped processes unless the containers were explicitly stopped.

The API receives `ConnectionStrings__DefaultConnection`, pointing to MySQL at `db:3306`. EF Core uses the Oracle `MySql.EntityFrameworkCore` provider and a scoped `AppDbContext`. API startup requires a nonempty connection string. When browser API calls are added, the API must enable CORS for the client origin.

Vite embeds public configuration at build time. The client image receives `VITE_API_URL` through `build.args`, using the configured API port. Setting a container environment variable after the build does not change the bundled URL. Never put secrets in `VITE_` variables.

Both services use HTTP inside the local Docker setup and publish ports on loopback only. Configure TLS at a reverse proxy and set deployment credentials before exposing the application beyond this machine.

## Configuration and data

The production Compose stack includes local default credentials, so copying an environment file is optional:

```sh
cp .env.example .env
```

Edit `.env` to change production Compose credentials or published ports, then rebuild and recreate the containers. It is ignored by Git. The default credentials are for local use only; production MySQL is available inside the Compose network and has no published host port. Database credentials are applied when the MySQL volume is first initialized. The development Compose file has fixed local credentials and does not use these `.env` settings. Changing development credentials requires a matching API connection string.

MySQL data persists in the existing `mysql_data` volume. Stop the services without deleting data with:

```sh
docker compose down
```

Do not use `down --volumes` unless you intend to delete database data. Old dependency and build-cache volumes from the previous development setup are no longer used.

## Develop with EF Core

Install the .NET 10 SDK to run the API or author migrations on your host. `api/Data/AppDbContext.cs` has no entities yet. Add each entity and its `DbSet` there when you implement the corresponding application feature. Create the first migration with the first entity.

Start the development database as described above. From the repository root, enter `api/`, then restore the project and the pinned EF tool:

```sh
cd api
dotnet restore
dotnet tool restore
```

Pass `-- --environment Development` to host EF commands so they load `appsettings.Development.json`. To override the connection string with .NET user secrets, initialize the project with `dotnet user-secrets init` and set `ConnectionStrings:DefaultConnection`.

After adding your first entity, create a migration:

```sh
dotnet ef migrations add InitialCreate -- --environment Development
```

Migration generation requires a valid connection string but does not need a running database. Commit the generated migration files. The API does not create tables or apply migrations at startup.

To apply migrations to the development database, run from `api/`:

```sh
dotnet ef database update -- --environment Development
```

The production Compose database has no host port. To apply migrations to that private database, run from the repository root while using the production mode:

```sh
docker compose --profile tools run --build --rm ef database update
```

`make migrate` runs the same command against the production Compose database. Stop the development apps and database before running it.

This command builds the SDK tools image with the current source and migrations, waits for a healthy database, and runs `dotnet ef database update` on the Compose network. The `ef` service runs only when requested through the `tools` profile. It does not change the API runtime image or mount source files. Use `--build` after each migration change so the command uses the current migration files.

## Image maintenance

Base images and MySQL are pinned by digest for repeatable builds. Periodically check their tags with `docker buildx imagetools inspect IMAGE:TAG`, update the corresponding digests in the Dockerfiles and both Compose files, and rebuild and test. Rebuilding alone does not advance a pinned image to a newer security update.

## License

MIT. See [LICENSE](LICENSE).
