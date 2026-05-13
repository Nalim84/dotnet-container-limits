# dotnet-container-limits

> Demonstrating why a container without resource limits can take down production.

This project shows in practice what happens when you run Docker containers
with and without resource limits — and how the kernel enforces cgroups.

---

## The Problem

By default, Docker sets **no resource limits** on containers.

This means a single container with a memory leak or CPU spike can:
- Consume all available host memory
- Starve other containers of CPU
- Trigger OOM (Out of Memory) kills on unrelated services
- Take down your entire production environment

This is not theoretical. It happens.

---

## How it Works

When you define resource limits in docker-compose:

```yaml
deploy:
  resources:
    limits:
      memory: 256m
      cpus: "0.50"
```

Docker translates these into **kernel cgroups** — the same Linux mechanism
that powers all container isolation. The kernel actively enforces these limits.

```
docker-compose limits
        ↓
    Docker daemon
        ↓
  kernel cgroups
        ↓
  Container process (enforced)
```

---

## Project Structure

```
/
├── src/
│   └── Api/
│       ├── Controllers/
│       │   ├── StressController.cs   # forces CPU and memory consumption
│       │   └── InfoController.cs     # shows cgroup limits in real time
│       ├── Program.cs
│       └── Api.csproj
├── docker-compose.yml                # default (development)
├── docker-compose.no-limits.yml      # ⚠ dangerous — no resource limits
├── docker-compose.with-limits.yml    # ✅ safe — cgroups enforced
├── Dockerfile
└── ContainerLimits.sln
```

---

## Running the Demo

**Prerequisites:** Docker Desktop

### Step 1 — Build the image

```bash
docker compose build
```

### Step 2 — Run WITHOUT limits (dangerous mode)

```bash
docker compose -f docker-compose.no-limits.yml up -d
```

Open a second terminal and watch resource usage:

```bash
docker stats
```

Now stress the container:

```bash
# Allocate 200MB of memory
curl -X POST "http://localhost:8080/api/stress/memory?megabytes=200"

# Stress CPU with 4 threads for 30 seconds
curl -X POST "http://localhost:8080/api/stress/cpu?seconds=30&threads=4"
```

**Observe:** the container uses resources freely. No enforcement.

```bash
docker compose -f docker-compose.no-limits.yml down
```

---

### Step 3 — Run WITH limits (safe mode)

```bash
docker compose -f docker-compose.with-limits.yml up -d
```

Check that cgroups are active:

```bash
curl http://localhost:8081/api/info
```

You should see `"mode": "WITH LIMITS (cgroups active)"`.

Now try the same stress:

```bash
# Try to allocate 200MB (limit is 256MB total)
curl -X POST "http://localhost:8081/api/stress/memory?megabytes=200"

# Stress CPU (limited to 50% of one core)
curl -X POST "http://localhost:8081/api/stress/cpu?seconds=30&threads=4"
```

Watch `docker stats` — the container cannot exceed the defined limits.
If memory exceeds 256MB, the kernel sends **OOMKilled** and restarts the container.

```bash
docker compose -f docker-compose.with-limits.yml down
```

---

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check |
| GET | `/api/info` | Container info + cgroup limits |
| GET | `/api/stress/stats` | Current process memory and CPU |
| POST | `/api/stress/memory?megabytes=50` | Allocate memory blocks |
| DELETE | `/api/stress/memory` | Release all allocated memory |
| POST | `/api/stress/cpu?seconds=10&threads=4` | CPU stress test |

Swagger UI available at: `http://localhost:8080/swagger`

---

## Key Concepts

### OOMKilled
When a container exceeds its memory limit, the Linux kernel's OOM Killer
terminates the process. Docker reports this as `OOMKilled: true` in
`docker inspect <container>`.

```bash
docker inspect api-with-limits | grep -i oom
```

### cgroups v2
Modern Linux kernels use cgroup v2. The limits are visible inside the container at:

```
/sys/fs/cgroup/memory.max   # memory limit
/sys/fs/cgroup/cpu.max      # CPU quota
```

The `/api/info` endpoint reads these values directly.

### Why this matters in high-throughput systems
In a microservices environment, multiple containers share the same host.
Without limits, one misbehaving service affects all others.
cgroups provide **multi-tenant isolation** at the kernel level.

---

## Architectural Decision (5W2H)

| Question | Decision |
|----------|----------|
| **What** | Demonstrate cgroups enforcement in a real .NET API |
| **Why** | Missing resource limits is one of the most common production mistakes |
| **Where** | docker-compose resource limits → kernel cgroups |
| **When** | Always — every container in production must have limits |
| **Who** | Backend/platform engineers responsible for container configuration |
| **How** | Two docker-compose files — one without limits, one with |
| **How Much** | 256MB / 0.5 CPU — reasonable for a lightweight .NET API |

---

## Related Posts

- [5W2H — The Framework I Use Before Writing a Single Line of Code](#)
- [What Really Happens When You Type docker run](#)
- [The Docker Lifecycle — From Build to Remove](#)
- [Why a Container Can Take Down Production](#) ← this project

---

## Tech Stack

- **.NET 8** — Web API
- **Docker** — Multi-stage build, Alpine Linux
- **docker-compose** — Resource limits via cgroups
- **Linux cgroups v2** — Kernel-level enforcement

---

## Author

**Alex Nalim** — Senior Backend Software Engineer
[LinkedIn](https://www.linkedin.com/in/alex-nalim/) · [GitHub](https://github.com/seu-usuario)