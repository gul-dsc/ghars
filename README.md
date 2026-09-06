# Ghars Platform

Ghars (غرس) is the Dubai Sports Council programme for instilling sporting values in club players
and their communities. This platform is the system of record for delivering and measuring that
programme: Dubai's sports clubs plan and log activities, request lectures and events from approved
government implementing entities, submit their annual performance indicators, and DSC reviews,
approves and reports on the results.

The application is bilingual throughout (English / Arabic with full RTL support) and is organised
around the approved programme documents held in [`docs/`](docs/).

**Main capabilities**

| Area | Purpose |
| --- | --- |
| Bookings | Clubs request lectures/events; the implementing entity proposes times; DSC confirms. |
| Agenda | The club's delivery record — what actually happened, with media evidence. |
| KPIs | Annual indicator submission by clubs, reviewed and approved by DSC, against 2026→2033 targets. |
| Surveys | The Official Program Survey (external, authoritative) plus an internal activity-feedback engine. |
| Gallery | Public showcase of programme media, published under DSC control. |
| Digital Library | Programme reference material for clubs. |
| Reports & Dashboards | Executive, club and partner views over the above. |
| Certificates | Participation certificates with public QR verification. |

## Technology

- **.NET 8** / **ASP.NET Core MVC** (Razor views, `Admin` area)
- **Entity Framework Core 8** (code-first migrations) on **SQL Server**
- **ASP.NET Core Identity** for authentication and role-based authorization
- **SignalR** for live notifications (`/hubs/notifications`)
- **Bootstrap 5.3** + Bootstrap Icons (CDN)
- **Chart.js** for dashboard and analytics charts
- **QuestPDF** for certificate generation, **QRCoder** for verification QR codes

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) or later
- SQL Server (Express, Developer or full) — LocalDB also works
- `dotnet-ef` tools, if you need to manage migrations:

  ```bash
  dotnet tool install --global dotnet-ef
  ```

## Local configuration

The committed `appsettings.json` contains a **development-only** connection string that uses Windows
authentication and carries no credentials:

```json
"DefaultConnection": "Server=.;Database=GharsPlatformDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

If that matches your machine, no further configuration is needed.

**Never put real credentials in `appsettings.json`.** To point at a different server, override
`DefaultConnection` outside source control by any of these means:

*.NET User Secrets (recommended for local development):*

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<host>;Database=GharsPlatformDb;User Id=<user>;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

*Environment variable (works for local runs, CI and servers):*

```bash
# bash
export ConnectionStrings__DefaultConnection="Server=<host>;Database=GharsPlatformDb;..."
```

```powershell
# PowerShell
$env:ConnectionStrings__DefaultConnection = "Server=<host>;Database=GharsPlatformDb;..."
```

*Server deployment:* supply the value through the hosting environment's protected configuration
(IIS configuration editor, container/environment secrets, or `appsettings.Production.json` kept
outside Git). `appsettings.Production.json` and `appsettings.Development.json` are both git-ignored.

Configuration precedence means the environment variable or user secret always wins over the file, so
you do not need to edit `appsettings.json` to change servers.

## Database setup

Create or update the schema from the migrations in [`Migrations/`](Migrations/):

```bash
dotnet build
dotnet ef database update
```

> Run `dotnet build` first. `dotnet ef database update --no-build` will silently apply **nothing** if
> the compiled assembly predates your latest migration — it still reports `Done.`

On first run the application seeds roles, reference organizations, demo clubs, seasons and sample
programme data automatically (see [`Data/DbSeeder.cs`](Data/DbSeeder.cs)), so a fresh database is
immediately usable.

## Run

```bash
dotnet run
```

The launch profile serves <https://localhost:60873> and <http://localhost:60874>.

## Roles

| Role | Scope |
| --- | --- |
| Super Admin | Full administrative access. |
| DSC Admin | Dubai Sports Council review and approval across all organizations. |
| Club Admin | One club's own bookings, agenda, KPIs and media. |
| Academy Admin | As Club Admin, for private academies. |
| Partner Admin | An implementing entity's own programmes and incoming booking requests. |
| Speaker | Lecturer/speaker profile and assigned activities. |
| Viewer | Read-only access. |

Organization identity is always derived server-side from the signed-in user's organization link; it
is never taken from a client-supplied value.

### Seed accounts

`DbSeeder` creates demo accounts under the `@ghars.local` domain with **hard-coded development
passwords**, listed in [`SEED_CREDENTIALS.md`](SEED_CREDENTIALS.md). They are intended for local
development and demonstration only.

> **Before any production deployment**, read the security note at the top of `SEED_CREDENTIALS.md`.
> Seeding is not currently gated by environment, so these accounts and passwords are created — and
> reset on every application start — in whatever environment the application runs.

## Documentation

| Document | Contents |
| --- | --- |
| [`handover.md`](handover.md) | Architecture, module-by-module walkthrough, conventions to preserve. |
| [`GHARS_REQUIREMENTS_GAP_ANALYSIS.md`](GHARS_REQUIREMENTS_GAP_ANALYSIS.md) | Approved requirements vs. the implementation. |
| [`GHARS_IMPLEMENTATION_PLAN.md`](GHARS_IMPLEMENTATION_PLAN.md) | The plan derived from that analysis. |
| [`GHARS_IMPLEMENTATION_REPORT.md`](GHARS_IMPLEMENTATION_REPORT.md) | What was built, decisions, risks, and the production-hardening pass. |
| [`GHARS_PRODUCTION_OPERATIONS.md`](GHARS_PRODUCTION_OPERATIONS.md) | Backup, restore, deployment, permissions and disaster recovery. |
| [`GHARS_REPOSITORY_BASELINE.md`](GHARS_REPOSITORY_BASELINE.md) | The verified production-candidate baseline for this repository. |
| [`docs/`](docs/) | The approved bilingual programme documents this platform implements. |

## Protected files

Sensitive uploads — KPI evidence, organization licences and official survey reports — are stored in
`protected-uploads/` at the **content root, outside `wwwroot`**, and are reachable only through
`ProtectedFilesController`, which re-checks authorization on every request.

`protected-uploads/` is deliberately **not** in source control. It is live business data, not source,
and the application creates the directories it needs on first use.

> A restored database without the matching restored protected files is an **incomplete restore**. The
> file endpoints report a missing file as an ordinary `404`, so the loss is silent. Backup and restore
> procedures — including a reconciliation query to detect it — are in
> [`GHARS_PRODUCTION_OPERATIONS.md`](GHARS_PRODUCTION_OPERATIONS.md).

Runtime uploads under `wwwroot/uploads/` (generated certificates, library files, gallery and agenda
media, logos) are excluded from Git for the same reason, with one exception: the sample library PDF
that `DbSeeder` references by a fixed path.
