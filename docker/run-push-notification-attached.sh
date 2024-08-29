docker run \
  --rm \
  --name push-notification \
  --env ASPNETCORE_ENVIRONMENT='production' \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  -p 8081:8080 \
  ghcr.io/homebase-id/odin-core-push-notification:pushnotification

# docker exec --interactive --tty push-notification bash
