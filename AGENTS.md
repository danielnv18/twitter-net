# Repository Guidelines

## Project Structure & Module Organization

- `api/` contains a .NET 10 ASP.NET Core API. Routes live in `Program.cs`; `api.http` provides sample requests.
- `client/src/` contains Vue components and TypeScript, with routing in `router/` and Pinia stores in `stores/`. Put static assets in `client/public/`.
- Client unit tests live in `client/src/__tests__/`; browser tests live in `client/e2e/`.
- Root `docker-compose.yml` connects the production API, compiled client, and MySQL. Each application has its own Dockerfile. `docker-compose.dev.yml` runs only the development database for host CLI development.

## Build, Test, and Development Commands

Run Compose commands from the repository root:

- `docker compose up --build`: build and start the production stack; client uses port 5173 and API uses 8080 by default.
- `docker compose down`: stop services, preserving database volumes.
- `docker compose config --quiet`: validate Compose configuration.
- `docker compose -f docker-compose.dev.yml up -d --wait`: start the separate development MySQL database on loopback port 3307.
- `docker compose -f docker-compose.dev.yml down`: stop the development database, preserving its volume.
- `docker compose -f docker-compose.dev.yml config --quiet`: validate development Compose configuration.
- `dotnet build api/api.csproj`: restore dependencies and build the API.
- `dotnet run --project api/api.csproj`: run the API in Development on port 5078 using its launch configuration and development connection string.

Preserve both run modes. Use Docker for the production stack and host `dotnet` and `npm` commands for development. Stop one mode before starting the other. Keep their MySQL volumes separate.

For client commands, first run `cd client` and `npm install`:

- `npm run dev`: start Vite.
- `npm run build`: type-check and build production assets.
- `npm run lint`: run Oxlint and ESLint with automatic fixes.
- `npm run format`: format client source with Oxfmt.

## Coding Style & Naming Conventions

Use two-space indentation, LF endings, single quotes, and no semicolons in client code. Prefer `<script setup lang="ts">`, PascalCase component filenames, and camelCase variables and functions. Name Pinia stores like `useCounterStore`.

For C#, preserve four-space indentation and existing brace placement. Use PascalCase for types and public members, and camelCase for locals and parameters.

## Testing Guidelines

Run client tests from `client/`: `npm run test:unit -- --run` uses Vitest, Vue Test Utils, and jsdom; `npm run test:e2e` uses Playwright. Install browsers with `npx playwright install` first. Playwright starts the development server automatically; CI requires a prior client build.

Name tests `*.spec.ts`. Add regression tests for changed behavior. No coverage threshold or API test project is configured yet.

## Commit & Pull Request Guidelines

History uses short subjects, such as `added example api`; no formal convention is established. Keep commits focused.

Pull requests should explain changes, link issues, list validation, and include screenshots for UI changes. Mention configuration or database changes.

## Security & Configuration

Use `.env.example` as the template for ignored local `.env` files. The root `.env` configures the production Compose stack only. Never commit secrets. Compose credentials are local defaults; MySQL data persists in separate named volumes for each mode. `api/appsettings.Development.json` matches the fixed development database credentials; environment variables or .NET user secrets can override that connection string. The API uses EF Core with MySQL through `ConnectionStrings__DefaultConnection`; `/health/ready` checks database connectivity. Treat `VITE_` variables as public browser configuration.
