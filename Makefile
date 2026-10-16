.DEFAULT_GOAL := help

.PHONY: help install dev migrate

help:
	@printf '%s\n' 'make install  Install npm dependencies, NuGet packages, and the EF tool.' 'make dev      Start development MySQL and run the host API and client.' 'make migrate  Apply EF migrations to the production Compose database.'

install:
	npm --prefix client install
	dotnet restore api/api.csproj
	cd api && dotnet tool restore

dev:
	@production_containers=$$(docker compose ps --status running --quiet) || exit $$?; \
	if [ -n "$$production_containers" ]; then \
		printf '%s\n' 'Stop the production stack with docker compose down before running make dev.' >&2; \
		exit 1; \
	fi
	docker compose -f docker-compose.dev.yml up -d --wait
	npm --prefix client run dev:all

migrate:
	docker compose --profile tools run --build --rm ef database update
