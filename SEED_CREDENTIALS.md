# Ghars Platform Demo Accounts

> ## ⚠ The passwords that used to be printed in this file are compromised
>
> This repository is **public**. Until 2026-09-06 this file listed five literal passwords, and
> `Data/DbSeeder.cs` contained the same values as compiled constants. They have been removed from
> both, but they remain in the public Git history of commits `4f1e77b` and `bb3772a`, which cannot be
> removed without rewriting published history.
>
> **Treat them as permanently public.** Any environment that was ever seeded with them — including a
> local development database created before this change — should have those accounts' passwords
> changed. On a development machine, `dotnet run -- reset-demo-passwords` does it in one step.

## What the seeder does now

| Environment | Demo accounts | Passwords on restart |
| --- | --- | --- |
| `Development` | Created, if a demo password is configured | Never changed |
| Anything else | **Not created at all** | Never changed |

Demo accounts are Development-only and cannot appear in production. Startup never resets an existing
user's password — a password changes only when a person changes it, or when an operator explicitly
runs the reset command below.

For the first administrator of a **production** installation, see the "Initial production
administrator" section of [`README.md`](README.md). That is a separate mechanism and has nothing to
do with the accounts described here.

## Enabling the demo accounts locally

All demo accounts share one password, which you choose and keep outside source control:

```bash
dotnet user-secrets init
dotnet user-secrets set "Ghars:Seed:DemoPassword" "<your own password>"
```

It must satisfy the Identity policy configured in `Program.cs`: at least 10 characters, with an
uppercase letter, a lowercase letter, a digit and a non-alphanumeric character.

Then run the application. Accounts that do not yet exist are created with that password.

If the setting is absent, the application still starts and logs a single warning; missing demo
accounts are simply not created, and the sample data that depends on them is skipped. Existing
accounts in an established database keep working either way.

An environment variable works too, for containers and CI:

```bash
export GHARS_SEED_DEMO_PASSWORD="<your own password>"
```

## Resetting a forgotten demo password

```bash
dotnet run -- reset-demo-passwords
```

This is an explicit operator action, never part of a normal start. It refuses to run outside the
`Development` environment, requires `Ghars:Seed:DemoPassword` to be set, and only ever touches
accounts in the `@ghars.local` domain — it cannot reach a real user account.

## The demo accounts

Every account below is created only in `Development`, only when a demo password is configured, and
all of them share that one password.

**Administrative**

- `superadmin@ghars.local` — Super Admin
- `dscadmin@ghars.local` — DSC Admin
- `admin1@ghars.local`, `admin2@ghars.local`, `admin3@ghars.local` — DSC Admin

**Clubs** — Club Admin, one per seeded club, plus `club1@ghars.local`

- `club-shabab-al-ahli-club@ghars.local`
- `club-al-nasr-club@ghars.local`
- `club-al-wasl-club@ghars.local`
- `club-hatta-club@ghars.local`
- `club-dubai-club-for-people-of-determination@ghars.local`
- `club-dubai-chess-culture-club@ghars.local`
- `club-al-habtoor-polo-club@ghars.local`

**Implementing entities** — Partner Admin, one per seeded government entity, named
`partner-<slug>@ghars.local`, where the slug is the lower-cased entity name with non-alphanumeric
characters replaced by hyphens. For example:

- `partner-digital-dubai@ghars.local`
- `partner-dubai-police@ghars.local`
- `partner-dubai-health-authority@ghars.local`

The full list follows from the entities in `SeedOrganizationsAsync` in
[`Data/DbSeeder.cs`](Data/DbSeeder.cs).
