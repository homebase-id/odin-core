
CPU_ARCHITECTURE=$(./.github/scripts/get-cpu-architecture.sh)

docker build \
    -f Docker/Dockerfile-identity-host \
    --build-arg CPU_ARCHITECTURE=$CPU_ARCHITECTURE \
    --tag dotyou:local .
