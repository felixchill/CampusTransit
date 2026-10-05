# CampusTransit — Real-Time Shuttle Tracking System

A campus transportation platform for real-time shuttle tracking, seat reservations, driver
reporting and fleet administration, modelled on the **University of Ghana, Legon** campus.
Built with ASP.NET Core, Blazor, SignalR and Entity Framework Core, running as a single
deployable web application on **SQLite** so it needs no external database server.

The seeded network uses the real map positions of campus landmarks — Balme Library, the
Great Hall, Legon Hall, Mensah Sarbah Hall, Akuafo Hall, Volta Hall, Commonwealth Hall, the
UG Business School, the School of Engineering and the Legon Botanical Gardens.

## Stack

| Concern | Technology |
| --- | --- |
| UI | Blazor Web App, interactive server rendering |
| Real-time | SignalR hub at `/hubs/transit` + a background telemetry simulator |
| API | ASP.NET Core minimal API under `/api` |
| Data | Entity Framework Core with SQLite (`campustransit.db`, created on first run) |
| Auth | Cookie authentication with role claims (Student, Faculty, Driver, Admin) |
| Map | Leaflet with OpenStreetMap tiles (no API key required) |
| Styling | Hand-written design system in `wwwroot/app.css` (light + dark), inline SVG icons |

## Run

```bash
dotnet run
```

The database is created and seeded automatically the first time the app starts. If the
seeded data set has changed since your last run, the old records are replaced on startup
automatically. Open <http://localhost:5137>.

## Demonstration accounts

| Role | Email | Password |
| --- | --- | --- |
| Student | `student@st.ug.edu.gh` | `Student@123` |
| Faculty | `faculty@ug.edu.gh` | `Faculty@123` |
| Driver | `driver@ug.edu.gh` | `Driver@123` |
| Administrator | `admin@ug.edu.gh` | `Admin@123` |

## Screens

- **Overview** (`/`) — network KPIs, live fleet table and route performance.
- **Live tracking** (`/map`) — interactive map with live vehicle markers, route filter and ETAs.
- **Routes & stops** (`/routes`) — published services, headways and stop sequences.
- **Seat reservations** (`/reserve`) — hold seats against live capacity; cancel when plans change.
- **Driver console** (`/driver`) — publish passenger counts and service status; file reports.
- **Administration** (`/admin`) — fleet register, route activation, passenger demand and reports.

## Real-time data flow

1. `ShuttleSimulator` (a `BackgroundService`) advances every in-service vehicle along its route
   polyline every two seconds, standing in for on-board GPS hardware.
2. `TransitService` pushes a `ShuttleUpdate` payload through the `TransitHub`.
3. Clients subscribed to `/hubs/transit` receive updates through the shared feed and per-route
   groups; the map redraws markers and the driver console republishes changes immediately.

## JSON API

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/routes` | Routes with ordered stops. |
| GET | `/api/shuttles` | Fleet with live positions and ETAs. |
| GET | `/api/shuttles/live?route=BAL` | Live telemetry, optionally filtered by route code. |
| GET | `/api/stats` | Network counters. |
| GET/POST/DELETE | `/api/reservations` | Requires authentication. |
| GET/POST | `/api/reports` | Requires the Driver or Admin role. |

## Hosting it for free

The app is a single container-friendly web application, so it runs on any platform that can
host a `Dockerfile`. Two free options are worth knowing, and they differ in one important way.

| | Render (free plan) | Azure App Service (F1 free) |
| --- | --- | --- |
| Cost | $0, no card required | $0, but signup usually needs a card |
| Filesystem | **Ephemeral** — no persistent disk on free | Persistent, so the SQLite file survives |
| Idle behaviour | Spins down after 15 min, ~40 s cold start | Always on, but 60 CPU-minutes/day (shared per region) |
| Deploys from | GitHub | GitHub Actions, `az`, or the VS/Rider publish profile |

### Render (fastest, no card)

1. Push the project to a GitHub repository (the folder is not a git repository yet — run
   `git init`, commit, and push).
2. In Render choose **New → Blueprint** and point it at the repository. `render.yaml` in the
   repository root defines the service, so there is nothing to configure by hand.
3. Wait for the first build. Render injects `PORT`, which the container already reads.

> **Data resets on the free plan.** Render free web services cannot attach a persistent disk,
> so `campustransit.db` is recreated every time the service restarts, redeploys, or wakes from
> its 15-minute sleep. See *Making the data survive* below — this is fixable in full.

### Making the data survive (Render's ephemeral disk)

Because a free Render service throws its filesystem away, the database has to live somewhere
else. The app already supports this: it uses SQLite while a connection string points at a file,
and **switches to PostgreSQL automatically** when a hosting platform injects a `DATABASE_URL`.

Using Neon (free, serverless Postgres, no credit card):

1. Create a project at <https://console.neon.tech> and copy the connection string. It looks like
   `postgresql://user:password@ep-xxx.eu-central-1.aws.neon.tech/neondb?sslmode=require`.
2. In the Render dashboard open the service, choose **Environment**, and add two variables:

   | Key | Value |
   | --- | --- |
   | `DATABASE_URL` | the Neon connection string |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |

3. Redeploy. The first start creates the schema and seeds the network; reservations now
   outlive restarts, redeploys and spin-downs.

No code change is needed on the platform side. The provider is chosen at startup and SQLite
stays the default for local development.

> The connection string must be a Neon/Postgres URL. Npgsql only understands `key=value`
> connection strings, so the app rewrites the `postgres://…` URI form (and maps `sslmode`)
> before handing it to the driver. The password is never written to the log — startup prints
> `Database provider: PostgreSQL (Host=…;Port=5432;Database=…)`.

### Render's 15-minute spin-down

This one cannot be removed for free without a trade-off, and it is worth understanding exactly
when it bites:

- A free service sleeps after **15 minutes with no inbound traffic** — and Render counts
  WebSocket messages as traffic. A Blazor Server page holds an open SignalR connection, so
  **while anyone has the app open it stays awake**. The simulation pause described below only
  stops outgoing work; the connection itself is what keeps the service warm.
- The pain is therefore the *first* request after 15 idle minutes: roughly 40 seconds of cold
  start. Open the URL once a minute before a demo and it will be warm for the audience.
- If always-instant matters more than convenience, either keep a free external pinger on
  `/healthz` every 10 minutes (this consumes most of Render's 750 free instance-hours per
  month), or use Azure App Service F1, which is always on but limited to 60 CPU-minutes a day.

### Azure App Service (data survives)

```bash
az webapp up --name campustransit --runtime "DOTNETCORE:10.0" --sku F1 --os-type Linux
```

Then switch **Configuration → General settings → Web sockets** to *On*, and set **HTTPS Only**
to *On* — the platform terminates TLS, and the app already trusts the forwarded scheme header.
The F1 plan gives 60 CPU-minutes per day, so keep an eye on **Quotas** in the portal.

### Keeping within a free CPU allowance

The shuttle simulation does no work while nobody is connected to the transit hub — no database
round trip, no broadcast — and resumes within one tick (2 seconds) of the first client
arriving. On the free tiers that is the difference between a permanently busy loop and an
almost idle process. The host log records each transition:

```
Shuttle simulation paused: no connected clients.
Shuttle simulation resumed: 1 client(s) connected.
```

### Health check

`GET /healthz` returns `200 {"status":"ok"}`. It is wired into `render.yaml` as the health
check path and is a useful smoke test after any deploy.

### Container notes

The image builds with the .NET 10 SDK and runs on the .NET 10 ASP.NET runtime as uid 1654
(non-root). `/app` is left writable so the database can be created on first start.

```bash
docker build -t campustransit .
docker run -p 8080:8080 campustransit
```

## Project layout

```
Data/        Entities, DbContext, seeder
Services/    TransitService (queries and commands), telemetry maths,
             ShuttleSimulator, UserContext
Hubs/        TransitHub — the SignalR endpoint
Endpoints/   Minimal API surface
Components/  Layout, shared primitives and routable pages
wwwroot/     Design system CSS and Leaflet interop modules
```
