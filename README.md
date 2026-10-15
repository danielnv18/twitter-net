# Twitter Net

A basic X/Twitter clone planned with a .NET API, a Vue.js client, and MySQL, running together through Docker Compose.

This repository contains a default Vue/Vite client and Docker development configuration. The .NET API is still a placeholder, and no X/Twitter application features are implemented.

## Structure

```text
api/                 # Future .NET API and development Dockerfile
client/              # Vue.js client and development Dockerfile
docker-compose.yml   # API, client, and MySQL services
.env.example         # Optional local configuration
LICENSE              # MIT license
```

## Run with Docker

Install Docker with Docker Compose, then run from the repository root:

```sh
docker compose up
```

On the first run, Docker builds the development images, installs the client dependencies, and initializes MySQL. The Vue client starts at <http://localhost:5173>. The API container prints a placeholder message and waits until a .NET project is added; it does not serve HTTP yet.

After adding a .NET project directly in `api/`, run:

```sh
docker compose up --build
```

The API starts at <http://localhost:8080> and the client at <http://localhost:5173>. If the placeholder containers are already running, stop them with `docker compose down` before starting again.

The API receives `ConnectionStrings__DefaultConnection`, pointing to MySQL at `db:3306`. The future API must use this connection string and enable CORS for the client origin. The client receives `VITE_API_URL`; its future code must read `import.meta.env.VITE_API_URL` when making API requests.

## Configuration

Compose includes local development defaults, so copying an environment file is optional:

```sh
cp .env.example .env
```

Edit `.env` to change credentials or published ports. It is ignored by Git. The default credentials are for local development only; MySQL is available inside the Compose network and has no published host port. Database credentials are applied when the MySQL volume is first initialized.

MySQL data persists in a named Docker volume. Stop the services with:

```sh
docker compose down
```

## License

MIT. See [LICENSE](LICENSE).
