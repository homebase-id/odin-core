docker run \
  --rm \
  --name push-notification \
  --env ASPNETCORE_ENVIRONMENT='production' \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  -p 8081:8080 \
  push-notification:local

# docker exec --interactive --tty push-notification bash
