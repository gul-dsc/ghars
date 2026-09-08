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
| Ghars Annual Report | One official report per club per sports season — indicators and delivered-activity table generated from the Agenda, narrative written by the club, reviewed and approved by DSC, then frozen. |
| Surveys | The Official Program Survey (external, authoritative) plus an internal activity-feedback engine. |
| Ghars Channel | The programme's content channel: club activity media aggregated from the Agenda, plus awareness and educational material published by implementing entities after DSC approval. (Formerly presented as "Gallery".) |
| Digital Library | Programme reference material for clubs — booklets, publications and documents. Distinct from the Ghars Channel, which carries visual media only. |
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

On first run the application seeds the Identity roles and an active season in every environment. In
`Development` it additionally seeds reference organizations, demo clubs and sample programme data, so
a fresh development database is immediately usable. See
[Startup seeding](#startup-seeding) below and [`Data/DbSeeder.cs`](Data/DbSeeder.cs).

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
| Club Admin | One club's own bookings, agenda, KPIs, annual report and channel media. |
| Academy Admin | As Club Admin, for private academies. |
| Partner Admin | An implementing entity's own programmes, incoming booking requests and Ghars Channel content. Also covers government partner entities — they need no separate role. |
| Speaker | Lecturer/speaker profile and assigned activities. |
| Viewer | Read-only access. |

Organization identity is always derived server-side from the signed-in user's organization link; it
is never taken from a client-supplied value.

## Startup seeding

Seeding is split by what the data is for, not by convenience:

| Runs | What it seeds |
| --- | --- |
| **Every environment** | Pending EF migrations, the seven Identity roles, and an active season. |
| **Every environment, guarded** | The initial administrator — only when one is configured *and* the installation has none. |
| **`Development` only** | Demo organizations, clubs, users, activities, bookings, KPIs, agenda, gallery, library and notifications. |

Every block is guarded by an existence check, so restarting adds nothing and changes nothing.

### Password behaviour

**Application startup never resets an existing user's password.** A password changes only when a
person changes it, or when an operator explicitly runs a reset command. Restarting the application —
in any environment, for any reason — leaves every credential exactly as it was.

### Initial production administrator

A brand-new production database has no users, so the first administrator is created from
configuration. Supply these before the first start:

| Configuration key | Environment variable |
| --- | --- |
| `Ghars:Bootstrap:AdminEmail` | `GHARS_BOOTSTRAP_ADMIN_EMAIL` |
| `Ghars:Bootstrap:AdminPassword` | `GHARS_BOOTSTRAP_ADMIN_PASSWORD` |
| `Ghars:Bootstrap:AdminFullName` (optional) | `GHARS_BOOTSTRAP_ADMIN_FULL_NAME` |

The password must meet the Identity policy in `Program.cs`: 10+ characters with upper case, lower
case, a digit and a non-alphanumeric character.

How it behaves:

- **Creation happens only when no Super Admin and no DSC Admin exists.** Once you have an
  administrator, this path does nothing, restart after restart.
- **If the values are absent, no account is created.** There is no default and no fallback — nothing
  guessable is ever produced. The application starts normally and logs a critical message explaining
  which variables to set.
- **If the email matches an existing account**, that account is granted the administrator role and its
  password is left untouched. Bootstrap cannot be used to take over someone's credentials.
- The password is never logged and never written to a committed file.

Supply it through the deployment environment, not source control, and **remove
`GHARS_BOOTSTRAP_ADMIN_PASSWORD` from the environment once the administrator has signed in and
changed the password.**

### Development demo data

Demo and sample data — organizations, clubs, demo users, bookings, KPIs, agenda, gallery — is seeded
**only** when the environment is `Development`. It cannot appear in production.

Demo accounts share one password that you choose and keep out of source control:

```bash
dotnet user-secrets set "Ghars:Seed:DemoPassword" "<your own password>"
```

Without it the application still starts; it logs one warning and does not create the missing demo
accounts. Account names, and how to recover a forgotten demo password with
`dotnet run -- reset-demo-passwords`, are in [`SEED_CREDENTIALS.md`](SEED_CREDENTIALS.md).

> **This repository is public, and earlier commits contain literal demo passwords.** Those values are
> permanently exposed. Any database seeded before 2026-09-06 should have its demo passwords rotated —
> see the notice at the top of `SEED_CREDENTIALS.md`.

## Contact page

`/Home/Contact` carries a public enquiry form. **This platform sends no email** — there is no SMTP
configuration and no `IEmailSender`. A submitted enquiry is stored in `ContactMessages` and announced
to DSC Admins and Super Admins through the existing in-app notification system; they read and close it
at **Admin → Contact Messages**, and reply from their own mailbox.

That is deliberate: a record in the database cannot be lost to a misconfigured mail server, and the
notification links straight to the enquiry.

The contact details shown beside the form are configuration-driven and have **no defaults** — an
unset value is simply not rendered, so the page never publishes an invented address or a mailbox
nobody reads. The form itself works either way.

| Configuration key | Environment variable |
| --- | --- |
| `Ghars:Contact:Email` | `GHARS_CONTACT_EMAIL` |
| `Ghars:Contact:Phone` | `GHARS_CONTACT_PHONE` |
| `Ghars:Contact:AddressEn` / `AddressAr` | `GHARS_CONTACT_ADDRESS_EN` / `_AR` |
| `Ghars:Contact:OfficeHoursEn` / `OfficeHoursAr` | `GHARS_CONTACT_OFFICE_HOURS_EN` / `_AR` |
| `Ghars:Contact:WebsiteUrl` | `GHARS_CONTACT_WEBSITE_URL` |

Spam protection is a hidden honeypot field plus a rate limit of **5 submissions per 10 minutes per
client IP** (`contact-form` policy in [`Program.cs`](Program.cs)). This is the only anonymous POST
endpoint in the application.

## Documentation

| Document | Contents |
| --- | --- |
| [`handover.md`](handover.md) | Architecture, module-by-module walkthrough, conventions to preserve. |
| [`GHARS_REQUIREMENTS_GAP_ANALYSIS.md`](GHARS_REQUIREMENTS_GAP_ANALYSIS.md) | Approved requirements vs. the implementation. |
| [`GHARS_IMPLEMENTATION_PLAN.md`](GHARS_IMPLEMENTATION_PLAN.md) | The plan derived from that analysis. |
| [`GHARS_IMPLEMENTATION_REPORT.md`](GHARS_IMPLEMENTATION_REPORT.md) | What was built, decisions, risks, and the production-hardening pass. |
| [`GHARS_PRODUCTION_OPERATIONS.md`](GHARS_PRODUCTION_OPERATIONS.md) | Backup, restore, deployment, permissions and disaster recovery. |
| [`GHARS_PRODUCTION_DEPLOYMENT_CHECKLIST.md`](GHARS_PRODUCTION_DEPLOYMENT_CHECKLIST.md) | Operator checklist to work through during a deployment. |
| [`SEED_CREDENTIALS.md`](SEED_CREDENTIALS.md) | Development demo accounts and how to enable them. |
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

Runtime uploads under `wwwroot/uploads/` (generated certificates, library files, Ghars Channel and
agenda media, logos) are excluded from Git for the same reason, with one exception: the sample library
PDF that `DbSeeder` references by a fixed path.

`wwwroot/uploads/agenda` and `wwwroot/uploads/channel` sit under `wwwroot` but are **denied to the
static-file middleware**: Ghars Channel visibility is decided per database row, so those files are
served only through `/protected-files/gallery/{id}`, which re-checks it on every request.
