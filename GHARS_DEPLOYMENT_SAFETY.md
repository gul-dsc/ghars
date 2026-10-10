# Ghars Platform — deployment safety

What the repository now enforces, what only an Azure DevOps administrator can confirm, and what a
staging environment needs before it exists. Read with
[GHARS_AZURE_PIPELINE_SETUP.md](GHARS_AZURE_PIPELINE_SETUP.md) and
[GHARS_PRODUCTION_OPERATIONS.md](GHARS_PRODUCTION_OPERATIONS.md).

---

## 1. What the repository enforces

| Protection | Where | Checked by |
|---|---|---|
| CI runs start only for `main`; pull requests never start a run (`pr: none`); no schedules | `azure-pipelines.yml` `trigger:` | `DeploymentSafetyTests` |
| The Deploy stage runs only when the build succeeded **and** the source branch is exactly `refs/heads/main` **and** the run is not pull-request validation | `azure-pipelines.yml` Deploy `condition:` | `DeploymentSafetyTests` evaluates the expression for main, feature, look-alike, tag and PR refs |
| Any stage using `Ghars-Production`, in any pipeline file in the repo, must carry the main-branch guard | all `*.yml` | `DeploymentSafetyTests` |
| `Deploy-Ghars.ps1` needs `-DeploymentTarget Production` or `Staging`; there is no default target and no default path | `deploy/Deploy-Ghars.ps1` | `DeploymentSafetyTests` |
| The target is checked before any file, IIS, ACL or database change | `deploy/Deploy-Ghars.ps1` | `DeploymentSafetyTests` (order check, plus 19 `-ValidateOnly` runs) |
| No other pipeline, deploy file or `appsettings*.json` names a production folder, host or environment | repo | `DeploymentSafetyTests` |

**Production target** must be exactly: site `C:\inetpub\ghars`, pool `GharsPlatform`, smoke test at
`https://ghars.dubaisc.ae/`.

**Staging target** is refused if any of these is true:

- the site folder is, contains, or is inside `C:\inetpub\ghars` (after resolving `..` and case)
- `-Source` is inside the production site
- the pool is `GharsPlatform`, or the identity is `IIS AppPool\GharsPlatform`
- the release history overlaps `D:\GharsReleases` or the production site
- the backup root overlaps `D:\GharsBackups`
- the database is named `GharsPlatformDb` or `ghars`
- the smoke-test host is `ghars.dubaisc.ae`

`-ValidateOnly` runs just this check and exits, for use before a real run.

### Manual runs

Azure DevOps lets anyone with *Queue builds* permission press **Run pipeline** and pick a branch.
The YAML cannot switch that off. With the guard:

- a manual run of **any branch other than `main`** builds and stops before Deploy;
- a manual run of **`main`** deploys, exactly as a push to `main` does. The approval check on
  `Ghars-Production` (section 2) is what stands in front of it.

No new trigger was added.

### What the repository cannot enforce

The guard reads `Build.SourceBranch`, which Azure DevOps sets from the branch actually checked out.
It does not protect against someone **editing the YAML on a branch** to remove the guard and then
running that branch — the pipeline runs the YAML from the branch being built. Only the environment
approval and the branch-control check below close that gap.

---

## 2. Azure DevOps settings an administrator must verify

None of these can be seen from the repository. **None has been verified.** An authorized Azure
DevOps administrator should confirm each and record who checked it and when.

| # | Setting | Where | Required value |
|---|---|---|---|
| 1 | Environment approval | Pipelines → Environments → **Ghars-Production** → Approvals and checks → *Approvals* | At least one named approver; "allow approvers to approve their own runs" **off** |
| 2 | Branch control | same page → *Branch control* | Allowed branches: `refs/heads/main` only; "verify branch protection" on. This is what blocks an edited YAML on another branch |
| 3 | Environment permissions | Environments → Ghars-Production → Security | Only the pipeline and named administrators have *User*/*Administrator*; no broad groups |
| 4 | Trigger override | Pipeline → Edit → ⋮ → Triggers | "Override the YAML continuous integration trigger" **off**; no PR validation; no schedule |
| 5 | Other pipelines | Pipelines → All | No other pipeline points at this repository or the `Ghars-SelfHosted` pool with production paths |
| 6 | Pipeline permissions | Pipeline → ⋮ → Manage security | *Queue builds* and *Edit build pipeline* limited to the deployment team |
| 7 | Agent pool | Project settings → Agent pools → **Ghars-SelfHosted** → Security / Pipeline permissions | Only this pipeline may use it — it runs on the production server |
| 8 | Service connection | Project settings → Service connections (GitHub) | Used by this pipeline only; no "grant access to all pipelines" |
| 9 | Branch protection on GitHub | github.com/gul-dsc/ghars → Settings → Branches → `main` | Pull request required, at least one review, no force push, no deletion |

Until 1 and 2 are confirmed, treat every push to `main` as an unattended production deployment.

---

## 3. Staging environment plan (not provisioned)

Staging must share nothing writable with production.

| Item | Staging value (proposed) | Must not be |
|---|---|---|
| Server | Separate server preferred. If the production server must be reused, everything below is still separate | — |
| IIS site | `GharsStaging`, physical path `C:\inetpub\ghars-staging` | `C:\inetpub\ghars` or anything inside it |
| Application pool | `GharsPlatform-Staging`, identity `IIS AppPool\GharsPlatform-Staging` | `GharsPlatform` |
| Host name | e.g. `ghars-staging.dubaisc.ae` (internal DNS), its own certificate binding | `ghars.dubaisc.ae` |
| Database | `GharsStagingDb`, own SQL login with rights on that database only | the production database or login |
| Upload roots | `C:\inetpub\ghars-staging\protected-uploads` and `...\wwwroot\uploads` | production upload roots, or a share of them |
| Release history | `D:\GharsStagingReleases` | `D:\GharsReleases` |
| Database backups | own folder, e.g. `D:\GharsStagingBackups` | `D:\GharsBackups` |
| Settings and secrets | `ASPNETCORE_ENVIRONMENT=Staging`; connection string and bootstrap admin as **staging pool** environment variables or `appsettings.Staging.json` in the staging site only | values copied from production; `Development` (it seeds demo accounts) |
| CSP | leave `Security__Csp__Mode` unset (report-only) | `Enforce` before reports are reviewed |
| Deployment | separate pipeline file (e.g. `azure-pipelines-staging.yml`) with its own environment `Ghars-Staging`, passing `-DeploymentTarget Staging` | the production pipeline, or `Ghars-Production` |
| Agent | separate agent or pool if on another server | — |
| Access | internal network or IP allow-list; staging accounts only | public exposure with production accounts |
| Data | anonymised or synthetic. If a production copy is needed, it needs separate approval and the same handling as production | an unapproved production copy |

Before the first staging deployment, run:

```powershell
.\Deploy-Ghars.ps1 -DeploymentTarget Staging -ValidateOnly -Source <drop>\site `
    -SitePath C:\inetpub\ghars-staging -AppPool GharsPlatform-Staging `
    -HealthCheckUrl https://ghars-staging.dubaisc.ae/ -ReleaseHistoryRoot D:\GharsStagingReleases
```

---

## 4. Database migrations

### Current behaviour

- `DbSeeder.SeedAsync` calls `Database.MigrateAsync()` on **every startup** (`Data/DbSeeder.cs`),
  before roles, the active season and the bootstrap administrator are seeded.
- The pipeline's SQL variables are blank, so `Deploy-Ghars.ps1` takes **no pre-deploy backup** and
  does **not** apply the generated `ghars-migrations.sql`. It warns in the log instead.
- The idempotent script is still generated and shipped in the artifact, so it can be reviewed.

### Risks

1. **No restore point.** A migration runs the moment the pool starts, with no backup taken by
   the deployment.
2. **Failure surfaces as an outage.** A failing migration fails startup (500.30), with the site
   already back online, instead of failing the deployment while the site is still offline.
3. **No review gate.** Any migration merged to `main` is applied to production without anyone
   looking at the SQL first.
4. **Code rollback does not undo schema.** Redeploying an older build against a migrated
   database can fail or misbehave. Only a restore helps.
5. **Every instance migrates.** A second instance, a staging site pointed at the wrong
   connection string, or a recycled pool would all try to apply migrations.

### Recommended process (needs separate approval; no change made)

1. Give the agent's service account `db_backupoperator` on the production instance, install
   `sqlcmd`, and set the four SQL variables in `azure-pipelines.yml`. The script then backs up
   before anything changes and applies the reviewed script while the site is offline.
2. Require that a pull request adding a migration includes the generated SQL for review.
3. Keep the `Ghars-Production` approval (section 2) as the explicit go/no-go, with the approver
   checking that the release notes say whether a migration is included.
4. Test the migration on staging against a recent restored copy first, and time it.
5. Later, consider an opt-out setting so that production startup checks that no migrations are
   pending and refuses to start, instead of applying them. That is an application change.

---

## 5. Rollback

- **Code:** re-run `Deploy-Ghars.ps1 -DeploymentTarget Production -Source D:\GharsReleases\rollback-<stamp> ...`
  (see GHARS_AZURE_PIPELINE_SETUP.md Part 10).
- **Schema:** restore the pre-deploy backup. There is none until section 4 step 1 is done.
- **File migrations:** `--rollback <manifest>` per tool, before any purge
  (GHARS_PRODUCTION_OPERATIONS.md §5).
