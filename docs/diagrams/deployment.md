# Deployment Architecture

The application is a **single-node deployment by design** — session/cache state is in-process
memory and the Quartz scheduler uses the default in-memory `RAMJobStore` (no clustering
configured). See [Deployment_Guide.pdf](../Deployment_Guide.pdf) for the full checklist; this
diagram shows the intended topology.

```mermaid
flowchart TB
    subgraph Internet["Internet / Corporate network"]
        Users["Employees, Managers, HR Admins<br/>(browser, EN/AR)"]
    end

    subgraph Edge["Reverse proxy (operator-provided)"]
        Proxy["TLS termination + X-Forwarded-* headers<br/>(nginx / IIS / cloud load balancer)"]
    end

    subgraph AppServer["Application host — single instance"]
        Kestrel["Kestrel (ASP.NET Core 8)"]
        App["PerformanceManagement.Web process"]
        Health["GET /health<br/>(unauthenticated liveness/readiness probe)"]
    end

    subgraph DataTier["Data tier"]
        Postgres[("PostgreSQL 16<br/>single database, migrated on startup")]
    end

    subgraph Outbound["Outbound dependencies"]
        Smtp["Organization SMTP relay"]
        Chromium["Bundled headless Chromium<br/>(PDF rendering, downloaded on first run)"]
    end

    Users -->|HTTPS| Proxy -->|HTTP + X-Forwarded-Proto/For| Kestrel --> App
    Proxy -.->|health check| Health
    App --> Postgres
    App --> Smtp
    App --> Chromium
```

## Startup sequence

```mermaid
flowchart LR
    Start(["Process starts"]) --> Guard{"Production +<br/>any default credential<br/>still unchanged?"}
    Guard -->|Yes| Abort(["Refuse to start<br/>(throws, logs which key)"])
    Guard -->|No| Migrate["Database.MigrateAsync()<br/>(applies pending EF Core migrations)"]
    Migrate --> Seed["DatabaseSeeder.SeedCoreAsync<br/>(idempotent: admin account, reference data)"]
    Seed --> Warmup["PdfRenderer.WarmupAsync()<br/>(headless Chromium launched in background)"]
    Warmup --> Listen(["Kestrel begins listening"])
```

## Environments

| Environment | HTTPS redirect / HSTS | Cookie `Secure` | Exception page | Notes |
|---|---|---|---|---|
| Development | Off | `SameAsRequest` | Developer exception page | Matches `.claude/launch.json`'s plain `http://localhost` dev server |
| Production | On | `Always` | Generic `/Error` page (EN/AR) | `ASPNETCORE_ENVIRONMENT=Production` |

## Single-node constraint

Do **not** run multiple replicas of this application without first: moving session/distributed
cache to a shared store, switching Quartz to a clustered/persistent `JobStore` (otherwise every
replica runs every scheduled job independently — duplicate annual-form generation, duplicate
reminder emails), and moving `Database.MigrateAsync()` out of app startup into a single, explicit
release step.
