using System.Security.Cryptography;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Data;

public static class DbSeeder
{
    /// <summary>Domain used by every demo account. Nothing outside it is ever touched by demo seeding.</summary>
    private const string DemoEmailDomain = "@ghars.local";

    // Configuration keys. Each is also readable as a flat environment variable, so an operator can
    // export GHARS_BOOTSTRAP_ADMIN_PASSWORD without knowing the ASP.NET "__" section convention.
    private const string BootstrapEmailKey = "Ghars:Bootstrap:AdminEmail";
    private const string BootstrapPasswordKey = "Ghars:Bootstrap:AdminPassword";
    private const string BootstrapFullNameKey = "Ghars:Bootstrap:AdminFullName";
    private const string DemoPasswordKey = "Ghars:Seed:DemoPassword";

    private const string BootstrapEmailEnv = "GHARS_BOOTSTRAP_ADMIN_EMAIL";
    private const string BootstrapPasswordEnv = "GHARS_BOOTSTRAP_ADMIN_PASSWORD";
    private const string BootstrapFullNameEnv = "GHARS_BOOTSTRAP_ADMIN_FULL_NAME";
    private const string DemoPasswordEnv = "GHARS_SEED_DEMO_PASSWORD";

    /// <summary>
    /// Startup seeding. Structural data and the bootstrap administrator run in every environment;
    /// demo/sample data runs only in Development.
    /// </summary>
    /// <remarks>
    /// Nothing here ever changes an existing user's password. Demo passwords are set once, when the
    /// account is created. See <see cref="ResetDevelopmentDemoPasswordsAsync"/> for the explicit
    /// developer recovery path.
    /// </remarks>
    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            await SeedRequiredDataAsync(db, roleManager, logger);

            if (environment.IsDevelopment())
            {
                await SeedDevelopmentDemoDataAsync(db, userManager, configuration, logger);
            }
            else
            {
                logger.LogInformation(
                    "Demo/sample seeding skipped: environment is {Environment}, not Development.",
                    environment.EnvironmentName);
            }

            // Last, so that in Development the demo administrators already count as "an administrator
            // exists". Running it earlier would log a critical "nobody can sign in" that the demo seed
            // then makes untrue a second later. In Production nothing precedes it, so the behaviour is
            // identical either way.
            await BootstrapAdministratorAsync(userManager, configuration, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DbSeeder error");
            throw;
        }
    }

    /// <summary>
    /// Data the application cannot function without, in any environment. Idempotent, and carries no
    /// credentials of any kind.
    /// </summary>
    private static async Task SeedRequiredDataAsync(AppDbContext db, RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        await db.Database.MigrateAsync();

        var roles = new[]
        {
            RoleNames.SuperAdmin,
            RoleNames.DscAdmin,
            RoleNames.ClubAdmin,
            RoleNames.AcademyAdmin,
            RoleNames.PartnerAdmin,
            RoleNames.Speaker,
            RoleNames.Viewer
        };

        foreach (var r in roles)
        {
            if (!await roleManager.RoleExistsAsync(r))
                await roleManager.CreateAsync(new IdentityRole(r));
        }

        // An active season is required reference data, not demo content: KPI, agenda, gallery and
        // booking submission all need a valid SeasonId, and BookingsController accepts only a season
        // with IsActive. A season-less database starts cleanly and then rejects every club submission.
        if (!await db.Seasons.AnyAsync())
        {
            var year = DateTime.UtcNow.Year;
            db.Seasons.Add(new Season
            {
                TitleEn = $"Season {year}-{year + 1}",
                TitleAr = $"الموسم {year + 1}-{year}",
                StartDate = new DateOnly(year, 8, 1),
                EndDate = new DateOnly(year + 1, 5, 31),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded the initial active season.");
        }
    }

    /// <summary>
    /// Creates the first administrator of a brand-new installation from configuration only.
    /// </summary>
    /// <remarks>
    /// Deliberate properties, in order of how badly each would hurt if it were missing:
    /// <list type="bullet">
    /// <item>Runs only when no Super Admin and no DSC Admin exists, so it can never disturb a live system.</item>
    /// <item>Has no default and no fallback. Absent configuration creates nothing — never a guessable account.</item>
    /// <item>Never logs the password, including inside Identity validation failures.</item>
    /// <item>If the email matches an existing account, grants the role but leaves the password alone,
    /// so this path cannot be used to take over someone's credentials.</item>
    /// </list>
    /// </remarks>
    private static async Task BootstrapAdministratorAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration, ILogger logger)
    {
        var existingSuperAdmins = await userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin);
        var existingDscAdmins = await userManager.GetUsersInRoleAsync(RoleNames.DscAdmin);
        if (existingSuperAdmins.Count > 0 || existingDscAdmins.Count > 0)
        {
            logger.LogDebug("Bootstrap administrator skipped: an administrator already exists.");
            return;
        }

        var email = ReadSetting(configuration, BootstrapEmailKey, BootstrapEmailEnv);
        var password = ReadSetting(configuration, BootstrapPasswordKey, BootstrapPasswordEnv);

        if (email is null && password is null)
        {
            logger.LogCritical(
                "This installation has no administrator and no bootstrap configuration, so nobody can sign in. " +
                "Set {EmailEnv} and {PasswordEnv} (or the configuration keys {EmailKey} and {PasswordKey}) and restart. " +
                "No default account has been created.",
                BootstrapEmailEnv, BootstrapPasswordEnv, BootstrapEmailKey, BootstrapPasswordKey);
            return;
        }

        if (email is null || password is null)
        {
            logger.LogError(
                "Bootstrap administrator configuration is incomplete: {Missing} is not set. Both the email and the " +
                "password are required. No account has been created.",
                email is null ? BootstrapEmailKey : BootstrapPasswordKey);
            return;
        }

        var existing = await userManager.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (existing is not null)
        {
            // The account exists but holds no administrative role. Grant the role; do not touch the
            // password — bootstrap must never be a way to seize an existing account.
            if (!await userManager.IsInRoleAsync(existing, RoleNames.SuperAdmin))
                await userManager.AddToRoleAsync(existing, RoleNames.SuperAdmin);

            logger.LogWarning(
                "Bootstrap: {Email} already existed, so it was granted {Role} and its password was left unchanged.",
                email, RoleNames.SuperAdmin);
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = ReadSetting(configuration, BootstrapFullNameKey, BootstrapFullNameEnv) ?? "Ghars Administrator",
            PreferredLanguage = "en",
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            // Descriptions only. Identity never echoes the password, but keep the shape explicit.
            logger.LogError(
                "Bootstrap administrator {Email} could not be created: {Errors}. No account exists; fix the " +
                "configuration and restart.",
                email, string.Join(", ", created.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, RoleNames.SuperAdmin);
        logger.LogWarning(
            "Bootstrap administrator {Email} created with role {Role}. Sign in, change the password, then remove " +
            "{PasswordEnv} from the deployment environment.",
            email, RoleNames.SuperAdmin, BootstrapPasswordEnv);
    }

    /// <summary>
    /// Reads a setting from configuration, falling back to a flat environment variable name.
    /// Returns null for absent or whitespace values so callers can treat "not configured" as one case.
    /// </summary>
    private static string? ReadSetting(IConfiguration configuration, string configurationKey, string environmentVariable)
    {
        var value = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(value)) value = configuration[environmentVariable];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Development-only demo content. Every block is guarded by an existence check, so repeated runs
    /// add nothing and change nothing.
    /// </summary>
    private static async Task SeedDevelopmentDemoDataAsync(AppDbContext db, UserManager<ApplicationUser> userManager, IConfiguration configuration, ILogger logger)
    {
        var demoPassword = ReadSetting(configuration, DemoPasswordKey, DemoPasswordEnv);
        if (demoPassword is null)
        {
            // Warn once here rather than once per account. Existing demo users still resolve normally,
            // so an established development database is unaffected by this.
            logger.LogWarning(
                "No demo password configured, so missing demo accounts will not be created. Set the environment " +
                "variable {EnvironmentVariable} to a password meeting the Identity policy, or supply {Key} from any " +
                "other configuration source. (User Secrets needs \"dotnet user-secrets init\" first — this project " +
                "carries no UserSecretsId, so \"dotnet user-secrets set\" alone fails.)",
                DemoPasswordEnv, DemoPasswordKey);
        }

        await EnsureUserAsync(userManager, "superadmin@ghars.local", "Super Admin", RoleNames.SuperAdmin, null, demoPassword, logger);
        await EnsureUserAsync(userManager, "dscadmin@ghars.local", "DSC Admin", RoleNames.DscAdmin, null, demoPassword, logger);
        await EnsureUserAsync(userManager, "admin1@ghars.local", "Ghars Council Admin 1", RoleNames.DscAdmin, null, demoPassword, logger);
        await EnsureUserAsync(userManager, "admin2@ghars.local", "Ghars Council Admin 2", RoleNames.DscAdmin, null, demoPassword, logger);
        await EnsureUserAsync(userManager, "admin3@ghars.local", "Ghars Council Admin 3", RoleNames.DscAdmin, null, demoPassword, logger);

        await SeedOrganizationsAsync(db, logger);
        await SeedOrgUsersAndLearningProgramsAsync(db, userManager, demoPassword, logger);
        await SeedLibraryAgendaKpiGalleryAsync(db, logger);
        await SeedComprehensiveDummyDataAsync(db, userManager, logger);
    }

    /// <summary>
    /// Explicit developer recovery for a forgotten demo password. Invoked as
    /// <c>dotnet run -- reset-demo-passwords</c>; never part of a normal start.
    /// </summary>
    /// <returns>A process exit code.</returns>
    public static async Task<int> ResetDevelopmentDemoPasswordsAsync(IServiceProvider services, IHostEnvironment environment)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        if (!environment.IsDevelopment())
        {
            logger.LogError(
                "reset-demo-passwords refused: the environment is {Environment}, not Development.",
                environment.EnvironmentName);
            return 1;
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var demoPassword = ReadSetting(configuration, DemoPasswordKey, DemoPasswordEnv);
        if (demoPassword is null)
        {
            logger.LogError(
                "reset-demo-passwords refused: no demo password is configured. Set the environment variable " +
                "{EnvironmentVariable}, or supply {Key} from any other configuration source, then run this again.",
                DemoPasswordEnv, DemoPasswordKey);
            return 1;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Scoped to the demo domain so this can never reach a real account, whatever the database holds.
        var demoUsers = await userManager.Users
            .Where(x => x.Email != null && x.Email.EndsWith(DemoEmailDomain))
            .ToListAsync();

        var reset = 0;
        foreach (var user in demoUsers)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, demoPassword);
            if (result.Succeeded) reset++;
            else logger.LogWarning("Could not reset {Email}: {Errors}", user.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        logger.LogInformation("Reset {Count} of {Total} demo account passwords.", reset, demoUsers.Count);
        return reset == demoUsers.Count ? 0 : 1;
    }

    /// <summary>
    /// Finds or creates a demo account. Creation needs <paramref name="demoPassword"/>; when it is
    /// null the account is skipped rather than created with a guessable one.
    /// </summary>
    /// <remarks>
    /// An existing account's password is never modified here. Callers already tolerate a null return.
    /// </remarks>
    private static async Task<ApplicationUser?> EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string fullName, string role, int? primaryOrganizationId, string? demoPassword, ILogger logger)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (user is null)
        {
            if (demoPassword is null)
            {
                logger.LogDebug("Demo account {Email} not created: no demo password configured.", email);
                return null;
            }

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                PreferredLanguage = "en",
                EmailConfirmed = true,
                PrimaryOrganizationId = primaryOrganizationId,
                CreatedAtUtc = DateTime.UtcNow
            };

            var create = await userManager.CreateAsync(user, demoPassword);
            if (!create.Succeeded)
            {
                logger.LogWarning("Failed to create demo user {Email}: {Errors}", email, string.Join(", ", create.Errors.Select(e => e.Description)));
                return null;
            }
        }

        if (primaryOrganizationId.HasValue && user.PrimaryOrganizationId != primaryOrganizationId)
        {
            user.PrimaryOrganizationId = primaryOrganizationId;
            await userManager.UpdateAsync(user);
        }

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);

        return user;
    }

    /// <summary>
    /// Creates the approved Ghars organizations from <see cref="GharsMasterData"/>.
    /// </summary>
    /// <remarks>
    /// A brand-new development database gets exactly the approved roster — the 7 clubs, the 17
    /// implementing entities and Dubai Sports Council — and nothing else. Organizations outside the
    /// roster are never created here, so a fresh database cannot reintroduce the ones an existing
    /// database had deactivated. Existing rows are matched by name and are only ever updated, never
    /// duplicated under a spelling variant.
    /// </remarks>
    private static async Task SeedOrganizationsAsync(AppDbContext db, ILogger logger)
    {
        var sort = 1;
        foreach (var item in GharsMasterData.All())
        {
            var org = await db.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NameEn == item.NameEn);
            if (org is null)
            {
                org = new Organization
                {
                    OrganizationType = item.Type,
                    NameEn = item.NameEn,
                    NameAr = item.NameAr,
                    Email = MakeSeedEmail(item.NameEn),
                    Phone = "0000000000",
                    AddressEn = "Dubai, United Arab Emirates",
                    AddressAr = "دبي، الإمارات العربية المتحدة",
                    Status = ApprovalStatus.Approved,
                    CreatedAtUtc = DateTime.UtcNow,
                    Notes = "Approved Ghars organization"
                };
                db.Organizations.Add(org);
                await db.SaveChangesAsync();
            }

            // The roster is authoritative for type, approval and logo. A row that drifted — a partner
            // deactivated by hand, a logo left on a placeholder — is brought back in line here.
            org.OrganizationType = item.Type;
            org.NameAr = item.NameAr;
            org.Status = ApprovalStatus.Approved;
            org.IsDeleted = false;
            org.LogoPath = item.LogoPath;

            if (item.Type == OrganizationType.Club) continue;

            var profile = await db.PartnerProfiles.FirstOrDefaultAsync(x => x.OrganizationId == org.Id);
            if (profile is null)
            {
                db.PartnerProfiles.Add(new PartnerProfile
                {
                    OrganizationId = org.Id,
                    OverviewEn = item.OverviewEn ?? "",
                    OverviewAr = item.OverviewAr ?? "",
                    IsFeatured = true,
                    FeatureSortOrder = sort,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                profile.OverviewEn = string.IsNullOrWhiteSpace(profile.OverviewEn) ? item.OverviewEn ?? "" : profile.OverviewEn;
                profile.OverviewAr = string.IsNullOrWhiteSpace(profile.OverviewAr) ? item.OverviewAr ?? "" : profile.OverviewAr;
                profile.IsFeatured = true;
                profile.FeatureSortOrder = profile.FeatureSortOrder <= 0 ? sort : profile.FeatureSortOrder;
            }
            sort++;
        }

        await db.SaveChangesAsync();
        logger.LogInformation(
            "Seeded the approved Ghars roster: {Clubs} clubs, {Partners} implementing entities and Dubai Sports Council.",
            GharsMasterData.Clubs.Count, GharsMasterData.Partners.Count);
    }


    private static async Task SeedOrgUsersAndLearningProgramsAsync(AppDbContext db, UserManager<ApplicationUser> userManager, string? demoPassword, ILogger logger)
    {
        var season = await db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).FirstAsync();

        // Approved implementing entities only. A partner admin account is never created for a
        // deactivated or legacy organization: the account would sign in to a dashboard scoped to an
        // entity no club can book, which is worse than having no account at all.
        var partnerOrganizations = await db.Organizations.ApprovedPartners().ToListAsync();

        foreach (var org in partnerOrganizations)
        {
            var name = org.NameEn;
            if (org is null) continue;

            var email = $"partner-{MakeSlug(name)}@ghars.local";
            var user = await EnsureUserAsync(userManager, email, $"{name} Partner Admin", RoleNames.PartnerAdmin, org.Id, demoPassword, logger);
            if (user is null) continue;
            await EnsureOrgLinkAsync(db, org.Id, user.Id, OrganizationType.OtherPartner);
            await SeedProgramsForPartnerAsync(db, season.Id, org, user.Id);
        }

        var clubs = await db.Organizations.ApprovedClubs().ToListAsync();

        foreach (var club in clubs)
        {
            var email = $"club-{MakeSlug(club.NameEn)}@ghars.local";
            var user = await EnsureUserAsync(userManager, email, $"{club.NameEn} Club Admin", RoleNames.ClubAdmin, club.Id, demoPassword, logger);
            if (user is not null) await EnsureOrgLinkAsync(db, club.Id, user.Id, OrganizationType.Club);
        }

        var shabab = clubs.FirstOrDefault(x => x.NameEn == "Shabab Al Ahli Club");
        if (shabab is not null)
        {
            var user = await EnsureUserAsync(userManager, "club1@ghars.local", "Shabab Al Ahli Club Admin", RoleNames.ClubAdmin, shabab.Id, demoPassword, logger);
            if (user is not null) await EnsureOrgLinkAsync(db, shabab.Id, user.Id, OrganizationType.Club);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureOrgLinkAsync(AppDbContext db, int organizationId, string userId, OrganizationType hint)
    {
        if (!await db.OrganizationAdminLinks.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == userId))
        {
            db.OrganizationAdminLinks.Add(new OrganizationAdminLink
            {
                OrganizationId = organizationId,
                UserId = userId,
                RoleHint = hint,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = userId
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedProgramsForPartnerAsync(AppDbContext db, int seasonId, Organization org, string partnerUserId)
    {
        var baseDate = new DateTime(DateTime.UtcNow.Year, 5, 15, 10, 0, 0, DateTimeKind.Utc);
        var programs = org.NameEn switch
        {
            "Ministry of Education" => new[]
            {
                ("Ghars values course", "دورة قيم غرس", ActivityType.Course, 0, 35, "A focused course on Ghars values, responsible behaviour, and public service culture.", "دورة مركزة حول قيم غرس والسلوك المسؤول وثقافة الخدمة العامة."),
                ("Youth development training program", "برنامج تدريبي لتنمية الشباب", ActivityType.TrainingProgram, 6, 40, "Hands-on training in innovation, teamwork, and digital transformation skills.", "تدريب عملي في الابتكار والعمل الجماعي ومهارات التحول الرقمي."),
                ("Values-based leadership workshop", "ورشة القيادة المبنية على القيم", ActivityType.Workshop, 12, 30, "Interactive workshop for building leadership behavior around Ghars values.", "ورشة تفاعلية لبناء السلوك القيادي حول قيم غرس.")
            },
            "Community Development Authority" => new[]
            {
                ("Community responsibility course", "دورة المسؤولية المجتمعية", ActivityType.Course, 2, 30, "Learning path on volunteering, inclusion, and community participation.", "مسار تعليمي حول التطوع والشمول والمشاركة المجتمعية."),
                ("Youth volunteering workshop", "ورشة تطوع الشباب", ActivityType.Workshop, 9, 25, "Practical workshop on planning meaningful volunteer initiatives.", "ورشة عملية لتخطيط مبادرات تطوعية مؤثرة.")
            },
            "Dubai Health Authority" => new[]
            {
                ("Wellbeing and prevention training", "تدريب الرفاه والوقاية", ActivityType.TrainingProgram, 4, 45, "Training program on wellbeing, prevention, and healthy lifestyle habits.", "برنامج تدريبي حول الرفاه والوقاية وأنماط الحياة الصحية."),
                ("Sports health awareness lecture", "محاضرة التوعية الصحية الرياضية", ActivityType.Lecture, 11, 80, "Awareness lecture about safe sports practice and health culture.", "محاضرة توعوية حول الممارسة الرياضية الآمنة والثقافة الصحية.")
            },
            "Dubai Culture" => new[]
            {
                ("Heritage and identity course", "دورة التراث والهوية", ActivityType.Course, 5, 35, "Course connecting sports values with Emirati culture and identity.", "دورة تربط القيم الرياضية بالثقافة والهوية الإماراتية."),
                ("Creative storytelling workshop", "ورشة السرد الإبداعي", ActivityType.Workshop, 14, 28, "Workshop for youth to communicate values through creative stories.", "ورشة للشباب للتعبير عن القيم من خلال السرد الإبداعي.")
            },
            "Dubai Police" => new[]
            {
                ("Community safety lecture", "محاضرة السلامة المجتمعية", ActivityType.Lecture, 7, 75, "Lecture on safety, prevention, and responsible citizenship.", "محاضرة حول السلامة والوقاية والمواطنة المسؤولة."),
                ("Positive behavior training", "تدريب السلوك الإيجابي", ActivityType.TrainingProgram, 16, 35, "Training program on discipline, respect, and positive conduct.", "برنامج تدريبي حول الانضباط والاحترام والسلوك الإيجابي.")
            },
            _ => new[]
            {
                ("Sustainability values course", "دورة قيم الاستدامة", ActivityType.Course, 3, 40, "Course on sustainability, energy awareness, and responsible behavior.", "دورة حول الاستدامة والوعي بالطاقة والسلوك المسؤول."),
                ("Future green skills workshop", "ورشة مهارات المستقبل الخضراء", ActivityType.Workshop, 13, 30, "Workshop on climate awareness and green future skills.", "ورشة حول الوعي المناخي ومهارات المستقبل الخضراء.")
            }
        };

        foreach (var p in programs)
        {
            if (await db.Activities.AnyAsync(x => x.CreatedByUserId == partnerUserId && x.TitleEn == p.Item1)) continue;
            var start = baseDate.AddDays(p.Item4);
            db.Activities.Add(new Activity
            {
                SeasonId = seasonId,
                PartnerOrganizationId = org.Id,
                Type = p.Item3,
                TitleEn = p.Item1,
                TitleAr = p.Item2,
                DescriptionEn = p.Item6,
                DescriptionAr = p.Item7,
                CategoryEn = "Learning Program",
                CategoryAr = "برنامج تعليمي",
                StartDateTime = start,
                EndDateTime = start.AddHours(2),
                LocationEn = org.NameEn,
                LocationAr = org.NameAr,
                Capacity = p.Item5,
                AllowWalkIn = false,
                Status = ActivityStatus.Published,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = partnerUserId
            });
        }
    }

    private static async Task SeedLibraryAgendaKpiGalleryAsync(AppDbContext db, ILogger logger)
    {
        var season = await db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).FirstAsync();
        if (!await db.LibraryCategories.AnyAsync(x => x.NameEn == "Ghars Awareness"))
        {
            db.LibraryCategories.AddRange(
                new LibraryCategory { NameEn = "Ghars Awareness", NameAr = "توعية غرس", SortOrder = 1, CreatedAtUtc = DateTime.UtcNow },
                new LibraryCategory { NameEn = "Educational Booklets", NameAr = "كتيبات تعليمية", SortOrder = 2, CreatedAtUtc = DateTime.UtcNow },
                new LibraryCategory { NameEn = "Awareness Videos", NameAr = "فيديوهات توعوية", SortOrder = 3, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var cat = await db.LibraryCategories.OrderBy(x=>x.SortOrder).FirstAsync();
        if (!await db.LibraryItems.AnyAsync(x => x.TitleEn == "Ghars Values Introduction"))
        {
            db.LibraryItems.AddRange(
                new LibraryItem { LibraryCategoryId = cat.Id, TitleEn = "Ghars Values Introduction", TitleAr = "مدخل إلى قيم غرس", DescriptionEn = "Introductory booklet for Ghars values and sports culture.", DescriptionAr = "كتيب تعريفي بقيم غرس والثقافة الرياضية.", PublishingEntityEn = "Dubai Sports Council", PublishingEntityAr = "مجلس دبي الرياضي", PublicationDate = new DateTime(2026,1,1), ContentType = LibraryContentType.EducationalBooklet, FilePath = "/uploads/library/sample-ghars-values.pdf", IsPublic = true, IsPublished = true, CreatedAtUtc = DateTime.UtcNow },
                new LibraryItem { LibraryCategoryId = cat.Id, TitleEn = "Positive Conduct Lecture", TitleAr = "محاضرة السلوك الإيجابي", DescriptionEn = "Lecture material for clubs and youth teams.", DescriptionAr = "مادة محاضرة للأندية وفرق الشباب.", PublishingEntityEn = "Dubai Police", PublishingEntityAr = "شرطة دبي", PublicationDate = new DateTime(2026,2,1), ContentType = LibraryContentType.Lecture, ExternalUrl = "https://www.dsc.gov.ae", CoverImagePath = "/img/brand/ghars-logo.png", IsPublic = true, IsPublished = true, CreatedAtUtc = DateTime.UtcNow },
                new LibraryItem { LibraryCategoryId = cat.Id, TitleEn = "Wellbeing Awareness Video", TitleAr = "فيديو توعوي عن الرفاه", DescriptionEn = "Awareness video provided by health partners.", DescriptionAr = "فيديو توعوي مقدم من شركاء الصحة.", PublishingEntityEn = "Dubai Health Authority", PublishingEntityAr = "هيئة الصحة بدبي", PublicationDate = new DateTime(2026,3,1), ContentType = LibraryContentType.AwarenessVideo, ExternalUrl = "https://www.dha.gov.ae", CoverImagePath = "/img/brand/ghars-logo.png", IsPublic = true, IsPublished = true, CreatedAtUtc = DateTime.UtcNow });
        }
        var club = await db.Organizations.ApprovedClubs().FirstOrDefaultAsync();
        if (club != null && !await db.KpiSubmissions.AnyAsync(x=>x.SeasonId==season.Id && x.OrganizationId==club.Id))
        {
            db.KpiSubmissions.Add(new KpiSubmission { SeasonId=season.Id, OrganizationId=club.Id, NumberOfLecturesActivities=6, NumberOfLecturers=4, NumberOfImplementingEntities=3, NumberOfParticipants=180, PlayerParticipationRate=60, AttendanceRate=82, EthicalValuesAdherenceRate=90, WeeklyTrainingMinutes=180, HealthyDietaryHabitsRate=80, SatisfactionRate=86, CommunityEventsCount=5, Status=KpiSubmissionStatus.Approved, SubmittedAtUtc=DateTime.UtcNow, ReviewedAtUtc=DateTime.UtcNow, CreatedAtUtc=DateTime.UtcNow });
            db.AgendaEntries.Add(new AgendaEntry { SeasonId=season.Id, OrganizationId=club.Id, ActivityType=ActivityType.Lecture, SubjectEn="Ghars Baseline Values Lecture", SubjectAr="محاضرة خط الأساس لقيم غرس", ActivityDate=new DateTime(2026,1,15), Category=AgendaTargetCategory.Players, LecturerName="DSC Lecturer", DepartmentOrOrganization="Dubai Sports Council", NumberOfParticipants=45, Status=AgendaEntryStatus.Submitted, CreatedAtUtc=DateTime.UtcNow });
            db.GalleryItems.Add(new GalleryItem { SeasonId=season.Id, OrganizationId=club.Id, TitleEn="Ghars Club Activity", TitleAr="نشاط غرس في النادي", DescriptionEn="Sample official gallery item.", DescriptionAr="عنصر تجريبي في المعرض الرسمي.", MediaType=GalleryMediaType.OfficialPhoto, FilePath="/img/brand/ghars-logo.png", MediaDate=new DateTime(2026,1,15), IsPublished=true, CreatedAtUtc=DateTime.UtcNow });
            if (!await db.MediaAlbums.AnyAsync(x => x.TitleEn == "Ghars Club Activity Album"))
            {
                var album = new MediaAlbum { SeasonId = season.Id, OrganizationId = club.Id, TitleEn = "Ghars Club Activity Album", TitleAr = "ألبوم نشاط غرس في النادي", DescriptionEn = "Album containing photos, video and PDF coverage for a Ghars activity.", DescriptionAr = "ألبوم يحتوي على صور وفيديو وملف PDF لتغطية نشاط غرس.", CoverImagePath = "/img/brand/ghars-logo.png", AlbumDate = new DateTime(2026,1,15), IsPublic = true, CreatedAtUtc = DateTime.UtcNow };
                album.Items.Add(new MediaItem { MediaType = MediaType.Image, FilePath = "/img/brand/ghars-logo.png", CaptionEn = "Official photo", CaptionAr = "صورة رسمية", SortOrder = 1, CreatedAtUtc = DateTime.UtcNow });
                album.Items.Add(new MediaItem { MediaType = MediaType.Video, VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", CaptionEn = "Activity video", CaptionAr = "فيديو النشاط", SortOrder = 2, CreatedAtUtc = DateTime.UtcNow });
                album.Items.Add(new MediaItem { MediaType = MediaType.Pdf, FilePath = "/uploads/library/sample-ghars-values.pdf", CaptionEn = "Activity PDF", CaptionAr = "ملف النشاط", SortOrder = 3, CreatedAtUtc = DateTime.UtcNow });
                db.MediaAlbums.Add(album);
            }
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded Ghars library, agenda, KPI and gallery sample data.");
    }


    private static async Task SeedComprehensiveDummyDataAsync(AppDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        var baseline = await EnsureSeasonAsync(db, 2026);
        var comparison = await EnsureSeasonAsync(db, 2027);
        var clubs = await db.Organizations.ApprovedClubs().ToListAsync();
        var partners = await db.Organizations.ApprovedPartners().Take(8).ToListAsync();
        var adminUser = await userManager.Users.FirstOrDefaultAsync(x => x.Email == "superadmin@ghars.local");
        var adminId = adminUser?.Id ?? "seed";

        // Make sure every selected partner has visible published programs.
        foreach (var partner in partners)
        {
            var partnerUser = await userManager.Users.FirstOrDefaultAsync(x => x.PrimaryOrganizationId == partner.Id);
            if (partnerUser != null) await SeedProgramsForPartnerAsync(db, baseline.Id, partner, partnerUser.Id);
        }
        await db.SaveChangesAsync();

        // Only offerings owned by an approved implementing entity. Without this the demo bookings are
        // drawn from every published activity in the database, including those left behind by
        // organizations that reconciliation deactivated — which quietly recreates, as booking data,
        // exactly the partners the roster removed.
        var approvedPartnerIds = await db.Organizations.ApprovedPartnerIds().ToListAsync();
        var activities = await db.Activities
            .Where(x => x.PartnerOrganizationId != null
                        && approvedPartnerIds.Contains(x.PartnerOrganizationId.Value)
                        && x.Status == ActivityStatus.Published)
            .OrderBy(x => x.Id)
            .Take(12)
            .ToListAsync();
        var clubUsers = await userManager.Users.Where(x => x.PrimaryOrganizationId != null).ToListAsync();
        var statuses = new[] { BookingStatus.PendingPartnerApproval, BookingStatus.Approved, BookingStatus.Rejected, BookingStatus.PartnerProposedNewTime, BookingStatus.Confirmed };
        var idx = 0;
        foreach (var activity in activities)
        {
            if (clubs.Count == 0) break;
            var club = clubs[idx % clubs.Count];
            var requester = clubUsers.FirstOrDefault(x => x.PrimaryOrganizationId == club.Id) ?? adminUser;
            if (requester == null) continue;
            var title = $"Seed booking {activity.Id}-{club.Id}";
            if (!await db.BookingRequests.AnyAsync(x => x.ActivityId == activity.Id && x.OrganizationId == club.Id))
            {
                var status = statuses[idx % statuses.Length];
                var booking = new BookingRequest
                {
                    ActivityId = activity.Id,
                    OrganizationId = club.Id,
                    PartnerOrganizationId = activity.PartnerOrganizationId,
                    RequestedByUserId = requester.Id,
                    RequestedSeats = 12 + (idx * 3 % 25),
                    Notes = title,
                    Status = status,
                    ConfirmedStartUtc = status == BookingStatus.Confirmed || status == BookingStatus.Approved ? activity.StartDateTime : null,
                    ConfirmedEndUtc = status == BookingStatus.Confirmed || status == BookingStatus.Approved ? activity.EndDateTime : null,
                    PartnerResponseNotes = status == BookingStatus.Rejected ? "Schedule unavailable for this date." : status == BookingStatus.PartnerProposedNewTime ? "Please select one of the proposed alternatives." : null,
                    DecisionAtUtc = status == BookingStatus.PendingPartnerApproval ? null : DateTime.UtcNow.AddDays(-idx),
                    DecidedByUserId = adminId,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-10 + idx),
                    CreatedByUserId = requester.Id
                };
                db.BookingRequests.Add(booking);
                await db.SaveChangesAsync();
                if (status == BookingStatus.PartnerProposedNewTime)
                {
                    db.BookingProposedTimeOptions.AddRange(
                        new BookingProposedTimeOption { BookingRequestId = booking.Id, ProposedStartUtc = activity.StartDateTime.AddDays(7), ProposedEndUtc = activity.EndDateTime.AddDays(7), Note = "Morning option", IsActive = true, CreatedByUserId = adminId },
                        new BookingProposedTimeOption { BookingRequestId = booking.Id, ProposedStartUtc = activity.StartDateTime.AddDays(10).AddHours(2), ProposedEndUtc = activity.EndDateTime.AddDays(10).AddHours(2), Note = "Afternoon option", IsActive = true, CreatedByUserId = adminId });
                }
                db.BookingAuditTrails.Add(new BookingAuditTrail { BookingRequestId = booking.Id, Action = "Seed " + status, OldValuesJson = "PendingPartnerApproval", NewValuesJson = status.ToString(), AtUtc = DateTime.UtcNow, ByUserId = adminId });
            }
            idx++;
        }

        // Seed attendance sessions, attendance records, and certificates so Attendance and Certificates are never empty.
        // Attendance sessions and certificates are anchored to an Activity, so direct entity-first
        // bookings (ActivityId == null) are skipped here rather than dereferenced.
        var approvedBookings = await db.BookingRequests
            .Include(x => x.Activity)
            .Where(x => x.ActivityId != null && (x.Status == BookingStatus.Approved || x.Status == BookingStatus.Confirmed))
            .OrderBy(x => x.Id)
            .Take(8)
            .ToListAsync();
        foreach (var booking in approvedBookings)
        {
            var bookingActivityId = booking.ActivityId!.Value;
            var session = await db.AttendanceSessions.FirstOrDefaultAsync(x => x.ActivityId == bookingActivityId);
            if (session == null)
            {
                session = new AttendanceSession
                {
                    ActivityId = bookingActivityId,
                    SessionStartUtc = booking.ConfirmedStartUtc ?? booking.Activity?.StartDateTime ?? DateTime.UtcNow.AddDays(-7),
                    SessionEndUtc = (booking.ConfirmedEndUtc ?? booking.Activity?.EndDateTime ?? DateTime.UtcNow.AddDays(-7).AddHours(2)),
                    Code = $"GHR-{booking.ActivityId:D4}",
                    QrToken = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
                    CreatedByUserId = adminId
                };
                db.AttendanceSessions.Add(session);
                await db.SaveChangesAsync();
            }

            var clubUserIds = clubUsers.Where(x => x.PrimaryOrganizationId == booking.OrganizationId).Select(x => x.Id).Take(3).ToList();
            if (!clubUserIds.Any()) clubUserIds = clubUsers.Take(3).Select(x => x.Id).ToList();
            foreach (var userId in clubUserIds)
            {
                if (!await db.AttendanceRecords.AnyAsync(x => x.AttendanceSessionId == session.Id && x.UserId == userId))
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        AttendanceSessionId = session.Id,
                        UserId = userId,
                        OrganizationId = booking.OrganizationId,
                        CheckInUtc = session.SessionStartUtc.AddMinutes(5),
                        Method = AttendanceMethod.Manual
                    });
                }
                if (!await db.Certificates.AnyAsync(x => x.ActivityId == booking.ActivityId && x.UserId == userId))
                {
                    var token = Guid.NewGuid().ToString("N");
                    db.Certificates.Add(new Certificate
                    {
                        ActivityId = bookingActivityId,
                        UserId = userId,
                        IssuedAtUtc = DateTime.UtcNow.AddDays(-2),
                        IssuedByUserId = adminId,
                        CertificateNo = ($"GHR-{DateTime.UtcNow:yyyyMMdd}-{booking.ActivityId:D4}-{Guid.NewGuid():N}").Substring(0, 30),
                        VerifyToken = token,
                        PdfPath = "/uploads/certificates/sample-ghars-certificate.pdf",
                        Status = CertificateStatus.Issued
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        foreach (var club in clubs)
        {
            if (!await db.KpiSubmissions.AnyAsync(x => x.SeasonId == baseline.Id && x.OrganizationId == club.Id))
            {
                db.KpiSubmissions.Add(new KpiSubmission
                {
                    SeasonId = baseline.Id, OrganizationId = club.Id, NumberOfLecturesActivities = 5 + idx, NumberOfLecturers = 3 + (idx % 4), NumberOfImplementingEntities = 2 + (idx % 5), NumberOfParticipants = 120 + idx * 15,
                    PlayerParticipationRate = 58 + idx % 20, AttendanceRate = 78 + idx % 15, EthicalValuesAdherenceRate = 88 + idx % 10, WeeklyTrainingMinutes = 150 + idx * 5,
                    WarningsAndRedCards = Math.Max(0, 12 - idx), DiabetesCases = idx % 3, HeartConditionCases = idx % 2, HypertensionCases = idx % 4, OtherLifestyleConditionCases = idx % 2,
                    HealthyDietaryHabitsRate = 78 + idx % 15, SatisfactionRate = 82 + idx % 12, CommunityEventsCount = 5 + idx % 6,
                    Status = KpiSubmissionStatus.Approved, SubmittedAtUtc = DateTime.UtcNow.AddDays(-30), ReviewedAtUtc = DateTime.UtcNow.AddDays(-20), ReviewedByUserId = adminId, CreatedAtUtc = DateTime.UtcNow.AddDays(-40)
                });
            }
            if (!await db.KpiSubmissions.AnyAsync(x => x.SeasonId == comparison.Id && x.OrganizationId == club.Id))
            {
                db.KpiSubmissions.Add(new KpiSubmission
                {
                    SeasonId = comparison.Id, OrganizationId = club.Id, NumberOfLecturesActivities = 7 + idx, NumberOfLecturers = 4 + (idx % 4), NumberOfImplementingEntities = 3 + (idx % 5), NumberOfParticipants = 160 + idx * 18,
                    PlayerParticipationRate = 65 + idx % 20, AttendanceRate = 82 + idx % 14, EthicalValuesAdherenceRate = 90 + idx % 9, WeeklyTrainingMinutes = 165 + idx * 5,
                    WarningsAndRedCards = Math.Max(0, 9 - idx), DiabetesCases = idx % 2, HeartConditionCases = 0, HypertensionCases = idx % 3, OtherLifestyleConditionCases = idx % 2,
                    HealthyDietaryHabitsRate = 82 + idx % 13, SatisfactionRate = 86 + idx % 10, CommunityEventsCount = 6 + idx % 5,
                    Status = KpiSubmissionStatus.Approved, SubmittedAtUtc = DateTime.UtcNow.AddDays(-10), ReviewedAtUtc = DateTime.UtcNow.AddDays(-5), ReviewedByUserId = adminId, CreatedAtUtc = DateTime.UtcNow.AddDays(-15)
                });
            }
            if (!await db.AgendaEntries.AnyAsync(x => x.SeasonId == baseline.Id && x.OrganizationId == club.Id && x.SubjectEn == "Ghars Sports Values Session"))
            {
                db.AgendaEntries.Add(new AgendaEntry { SeasonId = baseline.Id, OrganizationId = club.Id, ActivityType = ActivityType.Lecture, SubjectEn = "Ghars Sports Values Session", SubjectAr = "جلسة القيم الرياضية لغرس", ActivityDate = new DateTime(2026, 2, 10).AddDays(idx), Category = AgendaTargetCategory.Players, LecturerName = "Ghars Lecturer", DepartmentOrOrganization = "Dubai Sports Council", NumberOfParticipants = 40 + idx * 4, Status = AgendaEntryStatus.Submitted, CreatedAtUtc = DateTime.UtcNow });
                db.GalleryItems.Add(new GalleryItem { SeasonId = baseline.Id, OrganizationId = club.Id, TitleEn = club.NameEn + " Ghars Lecture", TitleAr = club.NameAr + " محاضرة غرس", DescriptionEn = "Seed lecture photo/video coverage.", DescriptionAr = "تغطية تجريبية لمحاضرة غرس.", MediaType = idx % 2 == 0 ? GalleryMediaType.Photo : GalleryMediaType.Video, FilePath = "/img/brand/ghars-logo.png", MediaDate = new DateTime(2026, 2, 10).AddDays(idx), IsPublished = true, CreatedAtUtc = DateTime.UtcNow });
                if (!await db.MediaAlbums.AnyAsync(x => x.TitleEn == club.NameEn + " Ghars Lecture Album"))
                {
                    var album = new MediaAlbum { SeasonId = baseline.Id, OrganizationId = club.Id, TitleEn = club.NameEn + " Ghars Lecture Album", TitleAr = club.NameAr + " ألبوم محاضرة غرس", DescriptionEn = "Photos, video and PDF coverage for a Ghars lecture.", DescriptionAr = "صور وفيديو وملف PDF لتغطية محاضرة غرس.", CoverImagePath = "/img/brand/ghars-logo.png", AlbumDate = new DateTime(2026, 2, 10).AddDays(idx), IsPublic = true, CreatedAtUtc = DateTime.UtcNow };
                    album.Items.Add(new MediaItem { MediaType = MediaType.Image, FilePath = "/img/brand/ghars-logo.png", CaptionEn = "Lecture photo", CaptionAr = "صورة المحاضرة", SortOrder = 1, CreatedAtUtc = DateTime.UtcNow });
                    album.Items.Add(new MediaItem { MediaType = MediaType.Video, VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", CaptionEn = "Lecture video", CaptionAr = "فيديو المحاضرة", SortOrder = 2, CreatedAtUtc = DateTime.UtcNow });
                    album.Items.Add(new MediaItem { MediaType = MediaType.Pdf, FilePath = "/uploads/library/sample-ghars-values.pdf", CaptionEn = "Lecture PDF", CaptionAr = "ملف المحاضرة", SortOrder = 3, CreatedAtUtc = DateTime.UtcNow });
                    db.MediaAlbums.Add(album);
                }
            }
            idx++;
        }
        await db.SaveChangesAsync();

        var cat = await db.LibraryCategories.OrderBy(x => x.SortOrder).FirstOrDefaultAsync();
        if (cat != null && !await db.LibraryItems.AnyAsync(x => x.TitleEn == "Ghars Club Implementation Guide"))
        {
            db.LibraryItems.AddRange(
                new LibraryItem { LibraryCategoryId = cat.Id, TitleEn = "Ghars Club Implementation Guide", TitleAr = "دليل تطبيق غرس في الأندية", DescriptionEn = "PDF guide for clubs to implement Ghars activities.", DescriptionAr = "دليل PDF للأندية لتطبيق أنشطة غرس.", PublishingEntityEn = "Ghars Program", PublishingEntityAr = "برنامج غرس", PublicationDate = new DateTime(2026, 4, 1), ContentType = LibraryContentType.EducationalBooklet, FilePath = "/uploads/library/ghars-club-guide.pdf", IsPublic = true, IsPublished = true, CreatedAtUtc = DateTime.UtcNow },
                new LibraryItem { LibraryCategoryId = cat.Id, TitleEn = "Healthy Lifestyle Awareness", TitleAr = "التوعية بنمط الحياة الصحي", DescriptionEn = "External health awareness resource.", DescriptionAr = "مورد خارجي للتوعية الصحية.", PublishingEntityEn = "Dubai Health Authority", PublishingEntityAr = "هيئة الصحة بدبي", PublicationDate = new DateTime(2026, 5, 1), ContentType = LibraryContentType.AwarenessVideo, ExternalUrl = "https://www.dha.gov.ae", CoverImagePath = "/img/brand/ghars-logo.png", IsPublic = true, IsPublished = true, CreatedAtUtc = DateTime.UtcNow });
        }

        // The official participant satisfaction survey: native, completed inside Ghars, one per season.
        // Nothing is seeded into ExternalSurveys any more — that entity is retained for historical
        // records only and should never gain a new row.
        await SeedOfficialSatisfactionSurveyAsync(db, baseline, clubs, logger);

        var firstClubUser = clubUsers.FirstOrDefault(x => clubs.Any(c => c.Id == x.PrimaryOrganizationId));
        var firstPartnerUser = clubUsers.FirstOrDefault(x => partners.Any(c => c.Id == x.PrimaryOrganizationId));
        await AddNotificationAsync(db, firstPartnerUser?.Id, "New club booking request", "طلب حجز جديد من نادٍ", "A club submitted a booking request awaiting your response.", "قام نادٍ بإرسال طلب حجز بانتظار ردك.", "/PartnerDashboard");
        await AddNotificationAsync(db, firstClubUser?.Id, "Partner proposed modification", "اقترح الشريك تعديلاً", "A partner proposed alternative times for your booking.", "اقترح الشريك أوقاتاً بديلة لحجزك.", "/ClubDashboard");
        await AddNotificationAsync(db, adminUser?.Id, "KPI submitted", "تم إرسال مؤشرات الأداء", "A club KPI submission is ready for review.", "يوجد إرسال مؤشرات أداء من نادٍ بانتظار المراجعة.", "/Admin/Kpi");
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded comprehensive Ghars dummy data for users, clubs, partners, bookings, KPI, agenda, library, gallery and notifications.");
    }

    /// <summary>
    /// The official Ghars participant satisfaction survey for a season, with demo responses so the
    /// satisfaction indicator has something to show in Development.
    /// </summary>
    /// <remarks>
    /// The responses are anonymous, deterministic and attributed to seeded agenda entries, exactly as
    /// real ones collected through a club's QR link would be. They exist so a reviewer can see the
    /// indicator working end to end; a production database seeds none of this, because
    /// <see cref="SeedDevelopmentDemoDataAsync"/> never runs outside Development.
    /// </remarks>
    private static async Task SeedOfficialSatisfactionSurveyAsync(AppDbContext db, Season season, List<Organization> clubs, ILogger logger)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(x =>
            x.Purpose == SurveyPurpose.OfficialSatisfaction && x.SeasonId == season.Id);

        if (survey is null)
        {
            survey = new Survey
            {
                Purpose = SurveyPurpose.OfficialSatisfaction,
                SeasonId = season.Id,
                ActivityId = null,
                TitleEn = OfficialSurveyTemplate.TitleEn,
                TitleAr = OfficialSurveyTemplate.TitleAr,
                DescriptionEn = OfficialSurveyTemplate.DescriptionEn,
                DescriptionAr = OfficialSurveyTemplate.DescriptionAr,
                IsActive = true,
                PublicToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Surveys.Add(survey);
            await db.SaveChangesAsync();

            db.SurveyQuestions.AddRange(OfficialSurveyTemplate.Questions(survey.Id));
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded the official Ghars satisfaction survey for {Season}.", season.TitleEn);
        }

        if (await db.SurveyResponses.AnyAsync(x => x.SurveyId == survey.Id)) return;

        var questions = await db.SurveyQuestions.Where(x => x.SurveyId == survey.Id).OrderBy(x => x.SortOrder).ToListAsync();
        if (questions.Count == 0) return;

        // Approved clubs only. A demo response attributed to a deactivated organization would add its
        // ratings to the season's headline percentage while that club is absent from every filter that
        // could explain where the ratings came from.
        var approvedClubIds = await db.Organizations.ApprovedClubs().Select(x => x.Id).ToListAsync();
        var agendaEntries = await db.AgendaEntries
            .Where(x => x.SeasonId == season.Id && approvedClubIds.Contains(x.OrganizationId))
            .OrderBy(x => x.Id)
            .ToListAsync();

        // Fixed rating pattern rather than a random one, so every developer's database reports the
        // same satisfaction percentage and a changed figure always means changed code.
        var ratings = new byte[] { 5, 4, 5, 5, 4, 3, 5, 4, 5, 4, 4, 5, 5, 3, 4, 5, 4, 5, 5, 4, 5, 4, 4, 5, 3, 5, 4, 5, 5, 4, 4, 5, 5, 4, 5, 4 };
        var comments = new[]
        {
            "Very useful session for our players.",
            "The lecturer explained the values clearly.",
            null,
            "Would like more practical activities.",
            null,
            "Well organised and on time."
        };

        for (var i = 0; i < ratings.Length; i++)
        {
            var entry = agendaEntries.Count == 0 ? null : agendaEntries[i % agendaEntries.Count];
            var response = new SurveyResponse
            {
                SurveyId = survey.Id,
                UserId = null,
                AgendaEntryId = entry?.Id,
                OrganizationId = entry?.OrganizationId ?? (clubs.Count == 0 ? null : clubs[i % clubs.Count].Id),
                SubmittedAtUtc = DateTime.UtcNow.AddDays(-30).AddHours(i * 7)
            };
            db.SurveyResponses.Add(response);
            await db.SaveChangesAsync();

            foreach (var q in questions)
            {
                var answer = new SurveyAnswer { SurveyResponseId = response.Id, SurveyQuestionId = q.Id };
                switch (q.QuestionType)
                {
                    case SurveyQuestionType.Stars:
                        // The second rating question runs a little below the first, so the per-question
                        // breakdown shows a spread instead of two identical columns.
                        answer.StarsValue = q.SortOrder == 1
                            ? ratings[i]
                            : (byte)Math.Max(1, ratings[(i + 3) % ratings.Length] - (i % 4 == 0 ? 1 : 0));
                        break;
                    case SurveyQuestionType.YesNo:
                        answer.BoolValue = ratings[i] >= 4;
                        break;
                    case SurveyQuestionType.Text:
                        answer.TextValue = comments[i % comments.Length];
                        break;
                }
                db.SurveyAnswers.Add(answer);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} demo responses for the official satisfaction survey.", ratings.Length);
    }

    private static async Task<Season> EnsureSeasonAsync(AppDbContext db, int startYear)
    {
        var title = $"Season {startYear}-{startYear + 1}";
        var season = await db.Seasons.FirstOrDefaultAsync(x => x.TitleEn == title);
        if (season == null)
        {
            season = new Season { TitleEn = title, TitleAr = $"الموسم {startYear + 1}-{startYear}", StartDate = new DateOnly(startYear, 8, 1), EndDate = new DateOnly(startYear + 1, 5, 31), IsActive = startYear == DateTime.UtcNow.Year, CreatedAtUtc = DateTime.UtcNow };
            db.Seasons.Add(season);
            await db.SaveChangesAsync();
        }
        return season;
    }

    private static async Task AddNotificationAsync(AppDbContext db, string? userId, string titleEn, string titleAr, string messageEn, string messageAr, string linkUrl)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        if (await db.Notifications.AnyAsync(x => x.TitleEn == titleEn && x.TargetUserId == userId)) return;
        var n = new Notification { TitleEn = titleEn, TitleAr = titleAr, MessageEn = messageEn, MessageAr = messageAr, Type = NotificationType.Info, TargetType = NotificationTargetType.User, TargetUserId = userId, LinkUrl = linkUrl, CreatedAtUtc = DateTime.UtcNow };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        db.NotificationDeliveries.Add(new NotificationDelivery { NotificationId = n.Id, UserId = userId, DeliveredAtUtc = DateTime.UtcNow });
    }

    private static string MakeSeedEmail(string source)
    {
        return MakeSlug(source) + "@ghars.seed.local";
    }

    private static string MakeSlug(string source)
    {
        var chars = source.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = new string(chars).Trim('-');
        while (normalized.Contains("--")) normalized = normalized.Replace("--", "-");
        return normalized;
    }
}
