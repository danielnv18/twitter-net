# Twitter Net

A Vue 3 client, .NET 10 API, and MySQL application with account signup, sign-in, a protected profile, and sign-out. Signup asks for a name, username, email, and password. Sign-in accepts a username or email. The profile displays the name and username.

## Run development

Install Docker with Compose, Make, .NET 10, and a Node.js version supported by `client/package.json`. From the repository root, run:

```sh
make install
make dev
```

`make dev` starts the development database, applies checked-in migrations, and starts the host API and Vite client. Open <http://localhost:5173>. Vite proxies `/api` to `127.0.0.1:5078`. Cookies and requests stay on the browser's current origin. The client has no browser API URL configuration or CORS requirement.

Stop the production stack with `docker compose down` before starting development. Press Ctrl+C to stop the host apps. Stop development MySQL before switching modes:

```sh
docker compose -f docker-compose.dev.yml down
```

Development MySQL uses loopback port 3307 and the separate `twitter-net-dev_mysql_data` volume. The database name, username, and password are `twitter_net_dev`, matching `api/appsettings.Development.json`. Environment variables or .NET user secrets can override `ConnectionStrings:DefaultConnection`. The API does not read the root `.env` file.

To run the components separately, start development MySQL, apply the schema, then start the host processes:

```sh
docker compose -f docker-compose.dev.yml up -d --wait
make migrate-dev
dotnet run --project api/api.csproj
```

In another terminal, run `npm --prefix client run dev`. API health is at <http://localhost:5078/health>, database readiness at <http://localhost:5078/health/ready>, and development OpenAPI at <http://localhost:5078/openapi/v1.json>.

## Run Docker on local HTTP

Stop the host API, Vite, and development database first. The production Compose stack runs compiled assets and uses the separate `twitter-net_mysql_data` volume. Copy `.env.example` to the ignored `.env` if you need different local credentials or ports.

For the first run, create the database and apply migrations before serving account requests:

```sh
docker compose up -d --wait db
make migrate
AUTH_ALLOW_INSECURE_HTTP=true docker compose up --build --wait
```

Open <http://localhost:5173>. Nginx proxies `/api` to the API. The explicit `AUTH_ALLOW_INSECURE_HTTP=true` setting enables local HTTP cookies with unprefixed names. Keep this setting out of public deployments. Reuse it when recreating local HTTP containers, or set it in your ignored local `.env`.

Production authentication requires HTTPS by default. Without the explicit local HTTP setting, HTTP account requests return a JSON `https_required` problem. Health endpoints remain available over HTTP.

Use these commands to inspect or stop the stack:

```sh
sh scripts/smoke-test.sh
docker compose ps
docker compose down
```

Both `down` commands preserve their database volumes. Do not delete volumes to apply a migration. After code changes, rebuild with `docker compose up --build --wait`, including the local HTTP setting when applicable. `make migrate` runs the current EF tools image against the private production database. The API never applies production migrations at startup. Readiness checks connectivity, rather than schema compatibility.

## Run Docker with HTTPS

The optional TLS overlay terminates HTTPS in Nginx and forwards the verified request scheme to the API. Put `fullchain.pem` and `privkey.pem` in a directory outside the repository. Make that directory and key readable by the container's `nginx` user through a dedicated group or ACL. Check its numeric identity with `docker compose run --rm --no-deps --entrypoint id client`. Keep the private key inaccessible to unrelated host users.

After applying migrations, run:

```sh
TLS_CERTIFICATE_DIR=/absolute/path/to/certificates AUTH_ALLOW_INSECURE_HTTP=false \
  docker compose -f docker-compose.yml -f docker-compose.tls.yml up --build --wait
```

Open <https://localhost:8443> with a certificate valid for your hostname. The overlay binds HTTPS to loopback port 8443. For a public deployment, explicitly set `HTTPS_BIND_ADDRESS` and `HTTPS_PORT`, provide a publicly trusted certificate, and configure the deployment firewall and hostname. Certificate issuance and renewal are deployment responsibilities.

Secure mode uses `__Host-` cookies with `Secure`, `HttpOnly`, `SameSite=Lax`, and `Path=/`, without `Domain`. The API trusts forwarded headers only from configured `Auth:KnownProxies` addresses, with a one-proxy limit. An empty list disables forwarded-header processing. Compose reserves `172.30.0.0/24`, and the trusted Nginx address is `172.30.0.10`. If that subnet conflicts with your network, change the Compose subnet, client address, and API trusted proxy setting together. Never configure arbitrary proxy trust. Nginx replaces incoming forwarded headers with its own peer address and scheme.

Both app containers run without root privileges, with read-only root filesystems, dropped capabilities, and temporary storage under `/tmp`. The database receives structured `Database__*` settings through a connection-string builder, so password punctuation cannot add connection options. No credentials belong in `VITE_` variables.

## Manage the schema and keys

Identity users, hashed server sessions, and Data Protection keys live in MySQL through `api/Data/AppDbContext.cs`. Create migrations from `api/`:

```sh
dotnet tool restore
dotnet ef migrations add DescriptiveName -- --environment Development
```

Commit generated migration files. Apply them with `make migrate-dev` or `make migrate` for the selected run mode.

Data Protection uses the stable application name `twitter-net-auth-v1`. Its keys persist in MySQL, so replacing an API container preserves valid cookies. Database access controls and backups must protect that table. By default, its key XML is not encrypted at rest. Before public deployment, mount a persistent certificate containing a private key read-only into the API container, and configure `Auth__KeyCertificatePath` and `Auth__KeyCertificatePassword` through your secret mechanism. The .NET certificate loader accepts PKCS#12 files. Keep the same certificate available across replacements, and retain old certificates during key rotation. The certificate and password must never be committed. Certificate protection encrypts newly generated keys. Existing plaintext key rows and backups require a separate controlled migration or rotation plan. Do not delete active keys merely to enable encryption.

## Verify account behavior

Run client checks from `client/`:

```sh
npm run build
npm run test:unit -- --run
npm run test:e2e
```

Playwright starts Vite automatically. Account browser tests require the host development API and migrated development database to be running. Install the matching browsers with `npx playwright install` if needed. CI requires a client build before the browser suite.

API tests use real MySQL and create a randomly named `auth_test_*` database for each test. They drop only databases they created. Never point the test connection at an application account database. Start a separate test server with a user allowed to create and drop databases:

```sh
docker run -d --name twitter-net-auth-test-db \
  -e MYSQL_ROOT_PASSWORD=auth_test_only -p 127.0.0.1:3308:3306 mysql:8.4
make test-api
```

The default test connection is loopback port 3308 with the test-only credentials above. Set `AUTH_TEST_MYSQL` to a dedicated server connection to override it. The project-oriented test entry point is `api.Tests/api.Tests.csproj`, which directly references the API. No solution file is required. To stop that test container without removing its data, run `docker stop twitter-net-auth-test-db`.

The suite covers normalization and concurrent uniqueness, CSRF on each mutation, authorization, lockout, rate limits, revoked-cookie replay, idle and absolute expiry, security-stamp changes, cookie attributes, key persistence, database outages, and request limits. See [the backend checklist](docs/auth-tasks.md) for scope and remaining deployment work.

## Authentication contract

`GET /api/auth/session` returns the public account or `null`, a CSRF token, and session deadlines. `GET /api/auth/me` requires a valid server session. `POST /api/auth/signup`, `/login`, and `/logout` require the current `X-CSRF-TOKEN` header. Refresh `/session` after an identity change. Authentication responses use `Cache-Control: no-store`.

Passwords contain 15 to 128 characters and are never trimmed. Identity hashes them with PBKDF2 at 210,000 iterations. Usernames contain 3 to 30 ASCII letters, digits, or underscores. Names contain 1 to 100 characters after trimming. Emails are trimmed, limited to 254 characters, and uniquely normalized. Conflict and login failures use generic messages.

A cryptographically random session identifier is protected in the authentication cookie. MySQL stores its SHA-256 hash. Each authenticated request checks the session and current security stamp. Sessions expire after eight hours or 30 minutes without authenticated requests. Activity can extend only the idle deadline. Sign-out deletes the current server session before clearing the cookie. Cookies are browser-session cookies, so there is no unrequested remember-me option.

Login locks an account for 15 minutes after five failures. Authentication mutations also have a per-client-IP limit of 20 per minute and a global concurrent-request limit of 32. IP limits run in process and require shared storage before scaling to multiple API replicas. Requests have a 16 KiB body limit. There is no email verification, password recovery, MFA, or account-management interface yet.

## License

MIT. See [LICENSE](LICENSE).
