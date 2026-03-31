#!/usr/bin/env bash
set -o errexit -o pipefail

docker compose -p homebase-dev -f compose.dev.yml down -v
