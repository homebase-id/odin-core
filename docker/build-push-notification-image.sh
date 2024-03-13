docker buildx build \
    -f Docker/Dockerfile-push-notification \
    --platform linux/arm64,linux/amd64 \
    --tag push-notification:local .

