# docker buildx build \
#     -f Docker/Dockerfile-setup-helper \
#     --platform linux/arm64,linux/amd64 \
#     --tag setup-helper:local .

docker build \
    -f Docker/Dockerfile-setup-helper \
    --tag setup-helper:local .
