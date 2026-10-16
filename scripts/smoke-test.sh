#!/bin/sh
set -eu

api_url=${API_URL:-http://localhost:${API_PORT:-8080}}
client_url=${CLIENT_URL:-http://localhost:${CLIENT_PORT:-5173}}

health=$(curl --fail --silent --show-error --max-time 10 "$api_url/health")
if [ "$health" != 'Healthy' ]; then
  printf 'Unexpected API health response: %s\n' "$health" >&2
  exit 1
fi

readiness=$(curl --fail --silent --show-error --max-time 10 "$api_url/health/ready")
if [ "$readiness" != 'Healthy' ]; then
  printf 'Unexpected API readiness response: %s\n' "$readiness" >&2
  exit 1
fi

client_html=$(curl --fail --silent --show-error --max-time 10 "$client_url/")
if ! printf '%s' "$client_html" | grep -q '<div id="app"></div>'; then
  printf 'Client did not serve the Vue application.\n' >&2
  exit 1
fi

runtime_entrypoint=$(printf '%s\n' "$client_html" | sed -n 's/.*src="\(\/assets\/[^"]*\.js\)".*/\1/p')
if [ -n "$runtime_entrypoint" ]; then
  curl --fail --silent --show-error --max-time 10 "$client_url$runtime_entrypoint" > /dev/null

  route_html=$(curl --fail --silent --show-error --max-time 10 "$client_url/docker-smoke-test-route")
  if [ "$route_html" != "$client_html" ]; then
    printf 'Client history routes did not return the application entry point.\n' >&2
    exit 1
  fi

  missing_status=$(curl --silent --show-error --max-time 10 --output /dev/null --write-out '%{http_code}' "$client_url/assets/docker-smoke-missing.js")
  if [ "$missing_status" != '404' ]; then
    printf 'Missing static assets returned HTTP %s instead of 404.\n' "$missing_status" >&2
    exit 1
  fi
fi

printf 'API health, database readiness, and client HTTP smoke tests passed.\n'
