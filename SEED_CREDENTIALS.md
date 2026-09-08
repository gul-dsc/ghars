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

All demo accounts share one password, which you choose and keep outside source control. The
environment variable is the supported way to supply it, and works identically for local runs,
containers and CI:

```bash
export GHARS_SEED_DEMO_PASSWORD="<your own password>"
```

```powershell
$env:GHARS_SEED_DEMO_PASSWORD = '<your own password>'
```

It must satisfy the Identity policy configured in `Program.cs`: at least 10 characters, with an
uppercase letter, a lowercase letter, a digit and a non-alphanumeric character.

Then run the application. Accounts that do not yet exist are created with that password.

Any other ASP.NET configuration source works too, under the key `Ghars:Seed:DemoPassword`. **.NET User
Secrets needs one extra step:** this project deliberately carries no `UserSecretsId`, so
`dotnet user-secrets set` on its own fails. Running `dotnet user-secrets init` first adds a
`UserSecretsId` to `GharsPlatform.csproj` — a change to a tracked file that you would then be asked to
commit. The environment variable avoids that, which is why it is the documented path.

**There is no fallback.** If no value is configured the application still starts and logs a single
warning; missing demo accounts are simply not created, and the sample data that depends on them is
skipped. Nothing guessable is ever substituted. Existing accounts in an established database keep
working either way.

## Resetting a forgotten demo password

```bash
dotnet run -- reset-demo-passwords
```

This is an explicit operator action, never part of a normal start. It refuses to run outside the
`Development` environment, requires the demo password to be configured (`GHARS_SEED_DEMO_PASSWORD`, or
`Ghars:Seed:DemoPassword` from any configuration source), and only ever touches accounts in the
`@ghars.local` domain — it cannot reach a real user account.

It is also the way to align a database whose accounts were created at different times. The seeder sets
a demo password once, when the account is created, and never again — so accounts created before you
last changed `GHARS_SEED_DEMO_PASSWORD` still carry the earlier value. That is deliberate: startup must
not rewrite credentials. This command is the explicit opt-in that makes them uniform.

## The demo accounts

Every account below is created only in `Development`, only when a demo password is configured, and
all of them share that one password.

**Administrative**

- `superadmin@ghars.local` — Super Admin
- `dscadmin@ghars.local` — DSC Admin
- `admin1@ghars.local`, `admin2@ghars.local`, `admin3@ghars.local` — DSC Admin

**Organizations** — one Club Admin per approved club and one Partner Admin per approved implementing
entity, plus the legacy `club1@ghars.local`. The addresses follow a pattern rather than a list:

| Role | Pattern |
| --- | --- |
| Club Admin | `club-<slug>@ghars.local` |
| Partner Admin | `partner-<slug>@ghars.local` |

`<slug>` is the organization's English name, lower-cased, with every run of non-alphanumeric
characters replaced by a hyphen.

**The roster itself is not duplicated here on purpose.** Which clubs and implementing entities exist —
and therefore which accounts are seeded — is decided by
[`Data/GharsMasterData.cs`](Data/GharsMasterData.cs), the single approved list that both the seeder and
`reconcile-organizations` read. A list copied into this file would go stale the first time the roster
changed, and a stale credentials document is worse than none.
