docker run \
  --rm \
  --name setup-helper \
  --env ASPNETCORE_ENVIRONMENT='production' \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  -p 8082:8080 \
  setup-helper:local

