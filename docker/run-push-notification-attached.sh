docker run \
  --rm \
  --name push-notification \
  --env ASPNETCORE_ENVIRONMENT='production' \
  --env ASPNETCORE_URLS='http://*:80;https://*:443' \
  -p 8081:80 \
  -p 4431:443 \
  push-notification:local

# docker exec identity-host du -h
# docker exec --interactive --tty identity-host bash
