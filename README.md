# Twitter Net

A basic X/Twitter clone planned with a .NET API, a Vue.js client, and MySQL, running together through Docker Compose. No X/Twitter application features are implemented yet.

## Structure

```text
api/                 # .NET 10 API and multi-stage Dockerfile
client/              # Vue.js client, multi-stage Dockerfile, and Nginx config
docker-compose.yml   # Built API, compiled client, and MySQL services
scripts/             # HTTP smoke tests for the running apps
.env.example         # Optional local configuration
LICENSE              # MIT license
```

## Run with Docker

Install Docker with Docker Compose, then run from the repository root:

```sh
docker compose up --build --wait
```

Docker publishes the API in Release mode and runs its DLL in an ASP.NET runtime image. The client build runs `npm ci` and `npm run build`, including TypeScript checking, then copies only `dist/` into an Nginx runtime image. Both apps run their compiled output; there are no source mounts, development servers, file watchers, or hot reload.

The command waits until all three services are healthy:

- Client: <http://localhost:5173>
- API health: <http://localhost:8080/health>

The API runs in `Production`, so its development OpenAPI route is unavailable. `GET /health` returns HTTP 200 with a plain-text `Healthy` body when the API is running. It is a liveness check; it does not test MySQL connectivity. MySQL has its own Compose health check, and the API starts after MySQL is healthy.

Verify the running apps with:

```sh
sh scripts/smoke-test.sh
docker compose ps
```

The smoke test checks API health, the client HTML and compiled JavaScript, Vue history route fallback, and missing static asset responses. For custom published ports, pass `API_PORT` and `CLIENT_PORT` to the script, or set `API_URL` and `CLIENT_URL` directly. For example: `API_PORT=8081 CLIENT_PORT=5174 sh scripts/smoke-test.sh`.

After changing either app or its dependencies, rebuild and recreate the containers with `docker compose up --build --wait`. Restarting an existing container does not rebuild the app. Commit client dependency changes with the updated `client/package-lock.json`.

## Container configuration

Both app containers run as non-root users with dropped Linux capabilities and privilege escalation disabled. Their root filesystems are read-only, with writable temporary storage in `/tmp`. Runtime images omit the .NET SDK, Node.js, source code, and development dependencies. Each app image includes a health check, including when run without Compose.

Nginx supports Vue history routes, caches hashed assets, and revalidates the HTML entry point. Container logs rotate at 10 MB per file with three files per service. Restart policies restart stopped processes unless the containers were explicitly stopped.

The API receives `ConnectionStrings__DefaultConnection`, pointing to MySQL at `db:3306`. Database connectivity is not implemented yet. When browser API calls are added, the API must enable CORS for the client origin.

Vite embeds public configuration at build time. The client image receives `VITE_API_URL` through `build.args`, using the configured API port. Setting a container environment variable after the build does not change the bundled URL. Never put secrets in `VITE_` variables.

Both services use HTTP inside the local Docker setup and publish ports on loopback only. Configure TLS at a reverse proxy and set deployment credentials before exposing the application beyond this machine.

## Configuration and data

Compose includes local development credentials, so copying an environment file is optional:

```sh
cp .env.example .env
```

Edit `.env` to change credentials or published ports, then rebuild and recreate the containers. It is ignored by Git. The default credentials are for local use only; MySQL is available inside the Compose network and has no published host port. Database credentials are applied when the MySQL volume is first initialized.

MySQL data persists in the existing `mysql_data` volume. Stop the services without deleting data with:

```sh
docker compose down
```

Do not use `down --volumes` unless you intend to delete database data. Old dependency and build-cache volumes from the previous development setup are no longer used.

## Image maintenance

Base images and MySQL are pinned by digest for repeatable builds. Periodically check their tags with `docker buildx imagetools inspect IMAGE:TAG`, update the corresponding digests in the Dockerfiles and Compose file, and rebuild and test. Rebuilding alone does not advance a pinned image to a newer security update.

## License

MIT. See [LICENSE](LICENSE).
