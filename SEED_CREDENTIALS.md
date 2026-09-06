# Ghars Platform Seed Credentials

> ## ⚠ Security notice — read before deploying
>
> **Classification: development / demo only.** These accounts exist to make a freshly seeded database
> immediately usable. They are not production bootstrap credentials and must never be treated as such.
>
> **The seeder is not gated by environment.** `Program.cs` calls `DbSeeder.SeedAsync` unconditionally on
> every application start, and `EnsureSeedPasswordAsync` **resets each seeded account's password back to
> the value below on every start**. Consequently:
>
> - These accounts are created in whatever environment the application runs, production included.
> - Changing one of these passwords in a running system does not stick — the next restart resets it.
> - The passwords are compiled into the application (`Data/DbSeeder.cs`), so this file is not the only
>   place they exist and deleting it would not make them secret.
>
> **Required before production go-live** (a deployment decision, deliberately not changed here):
> gate seeding behind `app.Environment.IsDevelopment()` or an explicit configuration flag, and create
> the real administrative account through a separate, controlled bootstrap path.

Use these accounts after database seeding in development/test environments.

## Super Admin
- superadmin@ghars.local / Ghars@2026#Super

## Council/Admin users
- dscadmin@ghars.local / Ghars@2026#Dsc
- admin1@ghars.local / Ghars@2026#Dsc
- admin2@ghars.local / Ghars@2026#Dsc
- admin3@ghars.local / Ghars@2026#Dsc

## Club users
All seeded club users use: `Ghars@2026#Club`

- club1@ghars.local / Ghars@2026#Club
- club-shabab-al-ahli-club@ghars.local / Ghars@2026#Club
- club-al-nasr-club@ghars.local / Ghars@2026#Club
- club-al-wasl-club@ghars.local / Ghars@2026#Club
- club-hatta-club@ghars.local / Ghars@2026#Club
- club-dubai-club-for-people-of-determination@ghars.local / Ghars@2026#Club
- club-dubai-chess-culture-club@ghars.local / Ghars@2026#Club
- club-al-habtoor-polo-club@ghars.local / Ghars@2026#Club

## Partner/entity users
All seeded partner/entity users use: `Ghars@2026#Partner`

Email pattern:
- partner-community-development-authority@ghars.local
- partner-digital-dubai@ghars.local
- partner-dubai-health-authority@ghars.local
- partner-dubai-culture@ghars.local
- partner-dubai-police@ghars.local
- partner-dubai-electricity-and-water-authority@ghars.local
- partner-dubai-civil-defence@ghars.local
- partner-dubai-corporation-for-ambulance-services@ghars.local

Password for all partner/entity users:
- Ghars@2026#Partner

These credentials are for local testing only and must not be used for production.
