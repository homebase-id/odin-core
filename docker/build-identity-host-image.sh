# docker buildx build \
#     -f docker/Dockerfile-identity-host \
#     --build-arg VERSION_TEXT="v1.2.3" \
#     --platform linux/arm64,linux/amd64 \
#     --tag dotyou:local .

docker build \
    -f docker/Dockerfile-identity-host \
    --build-arg VERSION_TEXT="v1.2.3" \
    --tag dotyou:local .
