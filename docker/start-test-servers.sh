#!/usr/bin/env bash
set -o errexit -o pipefail

docker compose -p homebase-test -f compose.test.yml up -d --remove-orphans