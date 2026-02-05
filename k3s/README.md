# Odin Hosting - k3s Deployment

This directory contains Kubernetes manifests for deploying Odin Hosting on k3s with:
- PostgreSQL database
- Redis cache
- Odin Hosting (2 replicas for zero-downtime deployments)

## Prerequisites

- k3s cluster running
- `kubectl` configured to connect to your cluster
- Docker installed (for building images)

---

## Installing k3s

k3s is a lightweight Kubernetes distribution. Installation takes about 30 seconds.

### Single Node (simplest)

```bash
# Install k3s (without Traefik - we use ServiceLB directly)
curl -sfL https://get.k3s.io | sh -s - --disable=traefik

# Wait for it to be ready
sudo k3s kubectl get nodes

# Copy kubeconfig for non-root usage
mkdir -p ~/.kube
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $USER:$USER ~/.kube/config
chmod 600 ~/.kube/config

# IMPORTANT: k3s installs kubectl as a symlink to k3s, which always looks at
# /etc/rancher/k3s/k3s.yaml first. Set KUBECONFIG to use your copied config:
echo 'export KUBECONFIG=~/.kube/config' >> ~/.bashrc  # or ~/.zshrc for zsh
source ~/.bashrc  # or: source ~/.zshrc

# Verify kubectl works
kubectl get nodes
```

### Multi-Node Cluster

**On the server (control plane):**
```bash
# Install server node (without Traefik)
curl -sfL https://get.k3s.io | sh -s - --disable=traefik

# Get the token for joining workers
sudo cat /var/lib/rancher/k3s/server/node-token
```

**On each worker node:**
```bash
# Replace K3S_URL with your server's IP
# Replace K3S_TOKEN with the token from the server
curl -sfL https://get.k3s.io | K3S_URL=https://<server-ip>:6443 K3S_TOKEN=<token> sh -
```

### Why --disable=traefik?

k3s installs Traefik by default, which grabs ports 80/443. We disable it because:
- We use ServiceLB directly (simpler for TCP passthrough)
- Avoids conflicts with other services on the host
- ServiceLB still works without Traefik

### Installation Options

```bash
# Install with specific version
curl -sfL https://get.k3s.io | INSTALL_K3S_VERSION=v1.29.0+k3s1 sh -s - --disable=traefik

# Install with Docker instead of containerd
curl -sfL https://get.k3s.io | sh -s - --disable=traefik --docker
```

### Verify Installation

```bash
# Check nodes are ready
kubectl get nodes

# Check system pods
kubectl get pods -n kube-system
```

### Uninstall

```bash
# On server
/usr/local/bin/k3s-uninstall.sh

# On worker nodes
/usr/local/bin/k3s-agent-uninstall.sh
```

**What gets removed:**
- k3s binary and symlinks (kubectl, crictl, etc.)
- k3s service files
- All running pods/containers
- Cluster data (`/var/lib/rancher/k3s/`)
- PVC data stored in `/var/lib/rancher/k3s/storage/`
- Network interfaces (flannel, cni)
- iptables rules created by k3s

**What survives:**
- hostPath volumes (e.g., `/identity-host/data/*`) - these are just host directories
- Docker images (k3s uses containerd, separate from Docker)
- Your kubeconfig copy (`~/.kube/config`) - now invalid, delete it

After reinstalling k3s, just `kubectl apply` again and your hostPath volumes are remounted.

### k3s vs Docker Swarm Init

| Docker Swarm | k3s |
|--------------|-----|
| `docker swarm init` | `curl -sfL https://get.k3s.io \| sh -` |
| `docker swarm join --token ...` | `curl -sfL https://get.k3s.io \| K3S_URL=... K3S_TOKEN=... sh -` |
| `docker swarm leave` | `/usr/local/bin/k3s-uninstall.sh` |

---

## Quick Start

```bash
# Deploy everything (build image + deploy)
./deploy.sh all

# Or step by step:
./deploy.sh build    # Build the Docker image
./deploy.sh deploy   # Deploy all resources
```

## Files

| File | Description |
|------|-------------|
| `namespace.yaml` | Creates the `odin` namespace |
| `postgres.yaml` | PostgreSQL StatefulSet with PVC |
| `redis.yaml` | Redis deployment with PVC |
| `odin-hosting.yaml` | Odin Hosting deployment (2 replicas) with LoadBalancer |
| `kustomization.yaml` | Kustomize configuration |
| `deploy.sh` | Deployment helper script |

---

## Architecture

```
Internet → Node IP:80/443 → ServiceLB → odin-hosting Pods (load balanced)
```

**ServiceLB** (built into k3s) handles external traffic:
- Binds to host ports 80/443
- Load balances across the 2 odin-hosting pods
- No separate ingress controller needed

The odin-hosting Service is type `LoadBalancer`, which triggers ServiceLB to expose it externally.

### Why ServiceLB is Enough

This setup uses k3s's built-in **ServiceLB** for load balancing because:

- We need **TCP passthrough** (not HTTP routing)
- The app handles **TLS termination** itself
- We have a **single backend service** (no hostname-based routing)
- ServiceLB can load balance directly to our pods

You'd need an ingress controller (like nginx-ingress) if you had multiple services that needed HTTP routing, path-based routing, or TLS termination at the ingress level.

---

## Single Node vs Multi-Node

### Single Node Setup

```
                    ┌─────────────────────────────────────────┐
                    │              Node 1                      │
Internet ──────────►│ ServiceLB (host ports 80/443)           │
                    │      │                                   │
                    │      ├──► odin-hosting pod 1            │
                    │      └──► odin-hosting pod 2            │
                    │                                          │
                    │      postgres pod                        │
                    │      redis pod                           │
                    └─────────────────────────────────────────┘
```

- ServiceLB binds ports 80/443 on the single node
- Load balances between the 2 odin-hosting pods via iptables
- All pods run on the same node
- **Single point of failure** (if the node dies, everything dies)

### Multi-Node Setup

```
                    ┌─────────────────────┐    ┌─────────────────────┐
                    │       Node 1        │    │       Node 2        │
Internet ──────────►│ ServiceLB ─────────────► │ ServiceLB           │
        │           │   │                 │    │   │                 │
        │           │   ▼                 │    │   ▼                 │
        └──────────►│ odin-hosting pod 1  │    │ odin-hosting pod 2  │
                    │ postgres pod        │    │                     │
                    │ redis pod           │    │                     │
                    └─────────────────────┘    └─────────────────────┘
```

- ServiceLB runs on **every node** (it's a DaemonSet)
- Each node binds ports 80/443 on its own IP
- You can hit **either** node's IP - both will work
- Traffic arriving at Node 2 can be forwarded to pods on Node 1 (and vice versa)
- Pod anti-affinity spreads odin-hosting pods across nodes
- If Node 2 dies, Node 1 still serves traffic

### Key Differences

| Aspect | Single Node | Multi-Node |
|--------|-------------|------------|
| ServiceLB instances | 1 | 1 per node |
| Entry points | 1 IP | Multiple IPs (need external LB) |
| Pod distribution | All on one node | Spread across nodes |
| Fault tolerance | None | Node-level redundancy |

---

## Multi-Node with Hetzner

With multiple nodes, you have multiple IPs but typically want a single DNS entry. Options:

### Option 1: Hetzner Load Balancer (Recommended)

The cleanest solution. Hetzner LB provides a single IP that distributes traffic across your nodes.

```
                         ┌─────────────────┐
        your-domain.com  │  Hetzner LB     │
Internet ───────────────►│  (single IP)    │
                         │                 │
                         └────────┬────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    ▼                           ▼
            ┌──────────────┐            ┌──────────────┐
            │   Node 1     │            │   Node 2     │
            │              │            │              │
            │ odin-pod-1   │            │ odin-pod-2   │
            │ postgres     │            │              │
            │ redis        │            │              │
            └──────────────┘            └──────────────┘
```

**Setup via Hetzner Cloud Console or CLI:**

```bash
# Using hcloud CLI
hcloud load-balancer create --name odin-lb --type lb11 --location fsn1

# Add targets (your nodes)
hcloud load-balancer add-target odin-lb --server node1
hcloud load-balancer add-target odin-lb --server node2

# Add services (TCP, not HTTP - app handles TLS)
hcloud load-balancer add-service odin-lb --protocol tcp --listen-port 80 --destination-port 80
hcloud load-balancer add-service odin-lb --protocol tcp --listen-port 443 --destination-port 443
```

**Hetzner LB config notes:**
- Protocol: **TCP** (not HTTP, since your app handles TLS)
- Health check: TCP on port 80 or 443
- Use private network if your nodes are on one (cheaper traffic)

### Option 2: Hetzner Floating IP + keepalived

Cheaper but more DIY. A floating IP that automatically moves to a healthy node.

```bash
# Install keepalived on both nodes
apt install keepalived
```

**/etc/keepalived/keepalived.conf on Node 1 (MASTER):**
```conf
vrrp_instance VI_1 {
    state MASTER
    interface eth0
    virtual_router_id 51
    priority 100
    advert_int 1
    authentication {
        auth_type PASS
        auth_pass secret
    }
    virtual_ipaddress {
        <floating-ip>/32
    }
}
```

**/etc/keepalived/keepalived.conf on Node 2 (BACKUP):**
```conf
vrrp_instance VI_1 {
    state BACKUP
    interface eth0
    virtual_router_id 51
    priority 50           # Lower priority
    advert_int 1
    authentication {
        auth_type PASS
        auth_pass secret
    }
    virtual_ipaddress {
        <floating-ip>/32
    }
}
```

If Node 1 dies, keepalived promotes Node 2 and the floating IP moves automatically.

### Comparison

| Option | Cost | Complexity | Failover Speed |
|--------|------|------------|----------------|
| Hetzner LB | ~5-6€/mo | Low | Instant (health checks) |
| Floating IP + keepalived | ~4€/mo | Medium | Few seconds |
| Single node | 0€ | None | Manual intervention |

### DNS Configuration

Point your domain to the load balancer IP, not the individual nodes:

```
# For app traffic (via LB)
your-domain.com        →  A  →  <hetzner-lb-ip>

# For SSH access (direct to nodes)
node1.your-domain.com  →  A  →  <node-1-ip>
node2.your-domain.com  →  A  →  <node-2-ip>
```

---

## Kubernetes Concepts (for Docker Swarm Users)

If you're coming from Docker Swarm, here's how the concepts map:

### Core Concepts

| Kubernetes | Docker Swarm Equivalent |
|------------|------------------------|
| Namespace | No direct equivalent (like a folder for resources) |
| Deployment | Service with `replicas` |
| StatefulSet | Service with persistent identity (for databases) |
| Pod | Container (but can have multiple containers) |
| Service | Service endpoint / load balancer |
| ConfigMap | Config files / environment |
| Secret | Docker secrets |
| PersistentVolumeClaim | Named volumes |

---

### namespace.yaml

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: odin
```

A **Namespace** is like a virtual cluster - it isolates resources. All our stuff goes in the `odin` namespace so it doesn't conflict with other apps. Swarm doesn't have this; it uses stack names as prefixes instead.

---

### postgres.yaml (explained section by section)

#### PersistentVolumeClaim
```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-pvc
  namespace: odin
spec:
  accessModes:
    - ReadWriteOnce      # Only one node can mount it
  resources:
    requests:
      storage: 10Gi      # Request 10GB of storage
```

**PVC** = "I need storage". Kubernetes finds or creates a volume to satisfy this. In Swarm, you'd use a named volume like `postgres_data:`.

#### Secret
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: postgres-secret
type: Opaque
stringData:                    # Plain text (base64 encoded automatically)
  POSTGRES_USER: odin
  POSTGRES_PASSWORD: changeme
```

**Secret** = Docker secrets. Stores sensitive data. Injected into containers as environment variables or files.

#### StatefulSet
```yaml
apiVersion: apps/v1
kind: StatefulSet              # For stateful apps (databases)
metadata:
  name: postgres
spec:
  serviceName: postgres        # Headless service name
  replicas: 1                  # Number of instances
  selector:
    matchLabels:
      app: postgres            # How to find pods belonging to this
  template:                    # Pod template (what to run)
    metadata:
      labels:
        app: postgres          # Label for selection
    spec:
      containers:
        - name: postgres
          image: postgres:16-alpine
          ports:
            - containerPort: 5432
          envFrom:
            - secretRef:
                name: postgres-secret    # Load all secrets as env vars
          volumeMounts:
            - name: postgres-data
              mountPath: /var/lib/postgresql/data
          resources:
            requests:          # Minimum resources guaranteed
              memory: "256Mi"
              cpu: "250m"      # 0.25 CPU cores
            limits:            # Maximum allowed
              memory: "1Gi"
              cpu: "1000m"     # 1 CPU core
          readinessProbe:      # Is it ready for traffic?
            exec:
              command: ["pg_isready", "-U", "odin"]
          livenessProbe:       # Is it still alive?
            exec:
              command: ["pg_isready", "-U", "odin"]
```

**StatefulSet** vs **Deployment**: StatefulSet gives each pod a stable identity (`postgres-0`, `postgres-1`). Used for databases where you need predictable names and ordered startup. In Swarm, you'd just use a service with 1 replica.

**Probes** (no Swarm equivalent):
- `readinessProbe`: "Can this pod receive traffic?" Failed = removed from load balancer
- `livenessProbe`: "Is this pod healthy?" Failed = pod gets restarted

#### Service (headless)
```yaml
apiVersion: v1
kind: Service
metadata:
  name: postgres
spec:
  selector:
    app: postgres        # Route to pods with this label
  ports:
    - port: 5432
  clusterIP: None        # Headless = no load balancing, direct DNS
```

**Service** = Internal DNS name + load balancer. Other pods connect to `postgres:5432`.

`clusterIP: None` makes it "headless" - DNS returns the pod IPs directly instead of a virtual IP. Good for databases where clients need sticky connections.

In Swarm, services automatically get DNS names like `postgres`.

---

### redis.yaml

Similar to postgres, but uses a **Deployment** instead of StatefulSet because Redis is simpler:

```yaml
apiVersion: apps/v1
kind: Deployment          # Stateless workload manager
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    # ... pod template
```

**Deployment** = Swarm service. Manages replicas, handles rolling updates, restarts failed pods.

---

### odin-hosting.yaml

#### ConfigMap
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: odin-hosting-config
data:
  ASPNETCORE_ENVIRONMENT: "Production"
```

**ConfigMap** = Non-sensitive configuration. Like environment variables in a Swarm stack file.

#### Deployment with Rolling Update
```yaml
apiVersion: apps/v1
kind: Deployment
spec:
  replicas: 2                    # Two instances for zero-downtime
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 0          # Never have less than 2 running
      maxSurge: 1                # Can temporarily have 3 during update
```

This is the key for **zero-downtime deployments**:
- Start with 2 pods
- During update: spin up 1 new pod (now 3 total)
- Wait until new pod is ready
- Kill 1 old pod (back to 2)
- Repeat until all pods are new

Swarm has `update_config` with similar options.

#### Pod Anti-Affinity
```yaml
affinity:
  podAntiAffinity:
    preferredDuringSchedulingIgnoredDuringExecution:
      - weight: 100
        podAffinityTerm:
          labelSelector:
            matchExpressions:
              - key: app
                operator: In
                values: ["odin-hosting"]
          topologyKey: kubernetes.io/hostname
```

This says "prefer to put odin-hosting pods on **different nodes**". If one node dies, the other pod keeps running. Swarm has `placement` constraints but not this level of control.

#### Service (LoadBalancer)
```yaml
apiVersion: v1
kind: Service
spec:
  type: LoadBalancer     # Exposed externally via ServiceLB
  selector:
    app: odin-hosting
  ports:
    - name: http
      port: 80
    - name: https
      port: 443
```

`type: LoadBalancer` tells k3s to expose this service externally. ServiceLB (built into k3s) binds the host's ports 80/443 and forwards traffic to the pods.

In Swarm, this is like `ports: ["80:80", "443:443"]`.

---

### kustomization.yaml

**Kustomize** is a built-in Kubernetes tool that bundles multiple YAML files together so you can apply them with one command.

Without kustomize:
```bash
kubectl apply -f namespace.yaml
kubectl apply -f postgres.yaml
kubectl apply -f redis.yaml
kubectl apply -f odin-hosting.yaml
```

With kustomize:
```bash
kubectl apply -k .    # Apply all resources listed in kustomization.yaml
```

The `kustomization.yaml` file just lists what to include:

```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization

namespace: odin

resources:
  - namespace.yaml
  - postgres.yaml
  - redis.yaml
  - odin-hosting.yaml
```

It can also do more advanced things like:
- Override values for different environments (dev/staging/prod)
- Add common labels to all resources
- Generate ConfigMaps/Secrets from files
- Patch specific fields

But for this setup, it's just a convenience to deploy everything at once. The Swarm equivalent would be having everything in a single `docker-compose.yml`.

---

### Quick Reference: Common Commands

| Action | Docker Swarm | Kubernetes |
|--------|--------------|------------|
| Deploy | `docker stack deploy -c compose.yml myapp` | `kubectl apply -k .` |
| List services | `docker service ls` | `kubectl get deployments -n odin` |
| List containers | `docker ps` | `kubectl get pods -n odin` |
| View logs | `docker service logs myapp_web` | `kubectl logs -n odin <pod-name>` |
| Scale | `docker service scale myapp_web=3` | `kubectl scale deployment odin-hosting -n odin --replicas=3` |
| Update | `docker service update myapp_web` | `kubectl rollout restart deployment odin-hosting -n odin` |
| Delete | `docker stack rm myapp` | `kubectl delete -k .` |

---

## Zero-Downtime Deployments

The deployment is configured for zero-downtime updates:

1. **2 replicas** - Always at least one pod running
2. **RollingUpdate strategy** - `maxUnavailable: 0, maxSurge: 1`
3. **Pod anti-affinity** - Pods prefer different nodes
4. **Readiness probes** - Traffic only sent to ready pods

To perform a rolling update:

```bash
# Rebuild image and restart pods
./deploy.sh build
./deploy.sh update

# Or manually
kubectl -n odin rollout restart deployment/odin-hosting
kubectl -n odin rollout status deployment/odin-hosting
```

## Configuration

### Secrets

Edit `odin-hosting.yaml` to configure:

```yaml
stringData:
  ConnectionStrings__Postgres: "Host=postgres;Port=5432;..."
  ConnectionStrings__Redis: "redis:6379"
```

**Important**: For production, use proper secret management (e.g., sealed-secrets, external-secrets).

### Environment Variables

Add configuration to the ConfigMap in `odin-hosting.yaml`:

```yaml
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  # Add more config here
```

## Commands

```bash
./deploy.sh build         # Build Docker image
./deploy.sh deploy        # Deploy with kustomize
./deploy.sh deploy-manual # Deploy without kustomize
./deploy.sh all           # Build image and deploy
./deploy.sh update        # Rolling update (zero-downtime)
./deploy.sh status        # Show deployment status
./deploy.sh delete        # Delete all resources
```

## Troubleshooting

### Check pod status
```bash
kubectl -n odin get pods
kubectl -n odin describe pod <pod-name>
kubectl -n odin logs <pod-name>
```

### Check services
```bash
kubectl -n odin get services
kubectl -n kube-system get pods -l svc.k3s.cattle.io/servicelb  # ServiceLB pods
```

### Verify connectivity
```bash
# Check the LoadBalancer external IP
kubectl -n odin get svc odin-hosting

# Test from outside
curl http://<node-ip>
```

## Backups

The host can reach cluster services directly, so you can run backups from the host.

### Manual backup

```bash
# Backup using kubectl exec (recommended - no version mismatch issues)
kubectl -n odin exec postgres-0 -- pg_dump -U odin odin > backup.sql

# Restore
kubectl -n odin exec -i postgres-0 -- psql -U odin odin < backup.sql
```

### Automated backup (host cron job)

```bash
# Edit crontab
crontab -e

# Add daily backup at 2am
0 2 * * * kubectl -n odin exec postgres-0 -- pg_dump -U odin odin > /backups/odin-$(date +\%Y\%m\%d).sql

# Optional: cleanup backups older than 7 days
0 3 * * * find /backups -name "odin-*.sql" -mtime +7 -delete
```

### Backup with compression

```bash
# Backup compressed
kubectl -n odin exec postgres-0 -- pg_dump -U odin odin | gzip > backup-$(date +%Y%m%d).sql.gz

# Restore compressed
gunzip -c backup-20240101.sql.gz | kubectl -n odin exec -i postgres-0 -- psql -U odin odin
```

### Why this works

The host is part of the cluster network and can reach internal services. However, external
machines cannot reach the ClusterIP services (postgres, redis) - only LoadBalancer services
(odin-hosting on ports 80/443) are exposed externally.

## Production Considerations

1. **Secrets**: Use proper secret management
2. **Storage**: Configure appropriate StorageClass for PVCs
3. **Resources**: Adjust CPU/memory limits based on load
4. **Monitoring**: Add Prometheus metrics and Grafana dashboards
5. **Backups**: Configure PostgreSQL backups (see above)
6. **TLS Certificates**: Ensure proper certificate management in the app
