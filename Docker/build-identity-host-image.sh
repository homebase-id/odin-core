
docker buildx build \
    -f Docker/Dockerfile-identity-host \
    --platform linux/arm64,linux/arm,linux/amd64 \
    --tag dotyou:local .

