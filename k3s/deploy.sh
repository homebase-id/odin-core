#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if kubectl is available
if ! command -v kubectl &> /dev/null; then
    log_error "kubectl is not installed or not in PATH"
    exit 1
fi

# Check if connected to a cluster
if ! kubectl cluster-info &> /dev/null; then
    log_error "Cannot connect to Kubernetes cluster. Please check your kubeconfig."
    exit 1
fi

# Build the Docker image
build_image() {
    log_info "Building Docker image..."
    cd "$REPO_ROOT"
    docker build \
        -f docker/Dockerfile-identity-host \
        --build-arg VERSION_TEXT="$(git describe --tags --always 2>/dev/null || echo 'dev')" \
        --tag dotyou:local .

    # For k3s, import the image into containerd
    # This is needed if k3s uses containerd as runtime
    if command -v k3s &> /dev/null; then
        log_info "Importing image to k3s containerd..."
        docker save dotyou:local | sudo k3s ctr images import -
    fi
}

# Deploy using kustomize
deploy_kustomize() {
    log_info "Deploying with kustomize..."
    kubectl apply -k "$SCRIPT_DIR"
}

# Deploy manually (without kustomize)
deploy_manual() {
    log_info "Deploying manually..."

    log_info "Creating namespace..."
    kubectl apply -f "$SCRIPT_DIR/namespace.yaml"

    log_info "Deploying PostgreSQL..."
    kubectl apply -f "$SCRIPT_DIR/postgres.yaml"

    log_info "Deploying Redis..."
    kubectl apply -f "$SCRIPT_DIR/redis.yaml"

    log_info "Deploying Odin Hosting..."
    kubectl apply -f "$SCRIPT_DIR/odin-hosting.yaml"
}

# Wait for deployments to be ready
wait_for_ready() {
    log_info "Waiting for deployments to be ready..."

    kubectl -n odin wait --for=condition=ready pod -l app=postgres --timeout=120s || log_warn "PostgreSQL not ready"
    kubectl -n odin wait --for=condition=ready pod -l app=redis --timeout=120s || log_warn "Redis not ready"
    kubectl -n odin wait --for=condition=ready pod -l app=odin-hosting --timeout=120s || log_warn "Odin Hosting not ready"
}

# Show status
show_status() {
    log_info "Deployment status:"
    echo ""
    kubectl -n odin get pods
    echo ""
    kubectl -n odin get services
}

# Perform rolling update (zero downtime)
rolling_update() {
    log_info "Performing rolling update..."
    kubectl -n odin rollout restart deployment/odin-hosting
    kubectl -n odin rollout status deployment/odin-hosting
}

# Main
case "${1:-deploy}" in
    build)
        build_image
        ;;
    deploy)
        deploy_kustomize
        wait_for_ready
        show_status
        ;;
    deploy-manual)
        deploy_manual
        wait_for_ready
        show_status
        ;;
    all)
        build_image
        deploy_kustomize
        wait_for_ready
        show_status
        ;;
    update)
        rolling_update
        ;;
    status)
        show_status
        ;;
    delete)
        log_warn "Deleting all resources in odin namespace..."
        kubectl delete -k "$SCRIPT_DIR" || true
        ;;
    *)
        echo "Usage: $0 {build|deploy|deploy-manual|all|update|status|delete}"
        echo ""
        echo "Commands:"
        echo "  build         - Build the Docker image"
        echo "  deploy        - Deploy using kustomize (default)"
        echo "  deploy-manual - Deploy without kustomize"
        echo "  all           - Build image and deploy"
        echo "  update        - Perform rolling update (zero downtime)"
        echo "  status        - Show deployment status"
        echo "  delete        - Delete all resources"
        exit 1
        ;;
esac

log_info "Done!"
