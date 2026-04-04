#!/usr/bin/env bash
set -o errexit -o pipefail

docker compose -p homebase-test -f compose.test.yml down -v
