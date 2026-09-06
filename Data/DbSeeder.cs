using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Data;

public static class DbSeeder
{
    // Development/test seed credentials are intentionally explicit so Club and Partner users are not ambiguous.
    // Change these before any staging or production deployment.
    private const string SuperAdminSeedPassword = "Ghars@2026#Super";
    private const string DscAdminSeedPassword = "Ghars@2026#Dsc";
    private const string ClubSeedPassword = "Ghars@2026#Club";
    private const string PartnerSeedPassword = "Ghars@2026#Partner";
    private const string AcademySeedPassword = "Ghars@2026#Academy";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

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
            }

            await EnsureUserAsync(userManager, "superadmin@ghars.local", "Super Admin", RoleNames.SuperAdmin, null, SuperAdminSeedPassword, logger);
            await EnsureUserAsync(userManager, "dscadmin@ghars.local", "DSC Admin", RoleNames.DscAdmin, null, DscAdminSeedPassword, logger);
            await EnsureUserAsync(userManager, "admin1@ghars.local", "Ghars Council Admin 1", RoleNames.DscAdmin, null, DscAdminSeedPassword, logger);
            await EnsureUserAsync(userManager, "admin2@ghars.local", "Ghars Council Admin 2", RoleNames.DscAdmin, null, DscAdminSeedPassword, logger);
            await EnsureUserAsync(userManager, "admin3@ghars.local", "Ghars Council Admin 3", RoleNames.DscAdmin, null, DscAdminSeedPassword, logger);

            await SeedOrganizationsAsync(db, logger);
            await SeedOrgUsersAndLearningProgramsAsync(db, userManager, logger);
            await SeedLibraryAgendaKpiGalleryAsync(db, logger);
            await SeedComprehensiveDummyDataAsync(db, userManager, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DbSeeder error");
            throw;
        }
    }

    private static async Task<ApplicationUser?> EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string fullName, string role, int? primaryOrganizationId, string seedPassword, ILogger logger)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (user is null)
        {
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

            var create = await userManager.CreateAsync(user, seedPassword);
            if (!create.Succeeded)
            {
                logger.LogWarning("Failed to create seed user {Email}: {Errors}", email, string.Join(", ", create.Errors.Select(e => e.Description)));
                return user;
            }
        }

        if (primaryOrganizationId.HasValue && user.PrimaryOrganizationId != primaryOrganizationId)
        {
            user.PrimaryOrganizationId = primaryOrganizationId;
            await userManager.UpdateAsync(user);
        }

        await EnsureSeedPasswordAsync(userManager, user, seedPassword, logger);

        if (!await userManager.IsInRoleAsync(user, role))
            await userManager.AddToRoleAsync(user, role);

        return user;
    }


    private static async Task EnsureSeedPasswordAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string seedPassword, ILogger logger)
    {
        // Keeps already-created local seed users loginable after new seed credentials are introduced.
        // This only affects users seeded by this file because EnsureUserAsync is only called for known seed users.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, seedPassword);
        if (!reset.Succeeded)
        {
            logger.LogWarning("Failed to reset seed password for {Email}: {Errors}", user.Email, string.Join(", ", reset.Errors.Select(e => e.Description)));
        }
    }

    private static async Task SeedOrganizationsAsync(AppDbContext db, ILogger logger)
    {
        var governmentEntities = new (string En, string Ar, string OverviewEn, string OverviewAr, string Logo)[]
        {
            ("Community Development Authority", "هيئة تنمية المجتمع في دبي", "Community programs and youth development initiatives for Dubai society.", "برامج مجتمعية ومبادرات لتنمية الشباب في دبي.", "/img/partners/community-development-authority.svg"),
            ("Digital Dubai", "هيئة دبي الرقمية", "Digital transformation, innovation, data and smart city learning programs.", "برامج في التحول الرقمي والابتكار والبيانات والمدينة الذكية.", "/img/partners/digital-dubai.svg"),
            ("Dubai Academic Health Corporation", "مؤسسة دبي الصحية الأكاديمية", "Health awareness, wellbeing and academic development programs.", "برامج التوعية الصحية والرفاه والتطوير الأكاديمي.", "/img/partners/dubai-academic-health-corporation.svg"),
            ("Dubai Civil Defence", "الإدارة العامة للدفاع المدني – دبي", "Safety, emergency readiness and prevention awareness programs.", "برامج السلامة والاستعداد للطوارئ والتوعية الوقائية.", "/img/partners/dubai-civil-defence.svg"),
            ("Dubai Corporation for Ambulance Services", "مؤسسة دبي لخدمات الإسعاف", "First aid, response readiness and community health training.", "التدريب على الإسعافات الأولية والجاهزية والاستجابة الصحية المجتمعية.", "/img/partners/dubai-ambulance.svg"),
            ("Dubai Courts", "محاكم دبي", "Legal awareness, civic responsibility and institutional values programs.", "برامج الوعي القانوني والمسؤولية المجتمعية والقيم المؤسسية.", "/img/partners/dubai-courts.svg"),
            ("Dubai Culture", "هيئة الثقافة والفنون في دبي", "Culture, heritage, creativity and identity learning experiences.", "تجارب تعليمية في الثقافة والتراث والإبداع والهوية.", "/img/partners/dubai-culture.svg"),
            ("Dubai Customs", "دائرة جمارك دبي", "Trade awareness, compliance and economic security programs.", "برامج التوعية التجارية والامتثال والأمن الاقتصادي.", "/img/partners/dubai-customs.svg"),
            ("Dubai Economy and Tourism", "دائرة الاقتصاد والسياحة بدبي", "Entrepreneurship, tourism culture and economic awareness programs.", "برامج ريادة الأعمال والثقافة السياحية والوعي الاقتصادي.", "/img/partners/dubai-economy-tourism.svg"),
            ("Dubai Electricity and Water Authority", "هيئة كهرباء ومياه دبي", "Sustainability, energy, water and climate awareness programs.", "برامج الاستدامة والطاقة والمياه والتوعية المناخية.", "/img/partners/dewa.svg"),
            ("Dubai Health Authority", "هيئة الصحة في دبي والمؤسسات التابعة لها", "Wellbeing, preventive health and sports culture learning programs.", "برامج الرفاه والصحة الوقائية والثقافة الرياضية.", "/img/partners/dha.svg"),
            ("Dubai Islamic Economy Development Centre", "دبي Islamic Economy Development Centre", "Ethics, values and Islamic economy awareness programs.", "برامج القيم والأخلاقيات والتوعية بالاقتصاد الإسلامي.", "/img/partners/default-partner.svg"),
            ("Dubai Judicial Institute", "معهد دبي القضائي", "Legal culture and responsible citizenship learning initiatives.", "مبادرات تعليمية في الثقافة القانونية والمواطنة المسؤولة.", "/img/partners/default-partner.svg"),
            ("Dubai Media Council", "مجلس دبي للإعلام", "Media literacy, responsible communication and creative content programs.", "برامج الثقافة الإعلامية والتواصل المسؤول والمحتوى الإبداعي.", "/img/partners/default-partner.svg"),
            ("Dubai Media Incorporated", "مؤسسة دبي للإعلام", "Media production, storytelling and public communication programs.", "برامج الإنتاج الإعلامي والسرد والتواصل العام.", "/img/partners/default-partner.svg"),
            ("Dubai Municipality", "بلدية دبي", "Environment, city services and community responsibility programs.", "برامج البيئة وخدمات المدينة والمسؤولية المجتمعية.", "/img/partners/default-partner.svg"),
            ("Dubai Police", "القيادة العامة لشرطة دبي", "Safety, citizenship, prevention and community security awareness.", "برامج السلامة والمواطنة والوقاية والأمن المجتمعي.", "/img/partners/default-partner.svg"),
            ("Dubai Public Prosecution", "النيابة العامة", "Legal awareness and social responsibility programs.", "برامج الوعي القانوني والمسؤولية الاجتماعية.", "/img/partners/default-partner.svg"),
            ("Dubai Sports Council", "مجلس دبي الرياضي", "Sports values, culture and youth development programs.", "برامج القيم الرياضية والثقافة وتنمية الشباب.", "/img/partners/default-partner.svg"),
            ("Dubai Statistics Center", "مركز دبي للإحصاء", "Data literacy, statistics and evidence-based decision programs.", "برامج الثقافة الإحصائية والبيانات واتخاذ القرار المبني على الأدلة.", "/img/partners/default-partner.svg"),
            ("Dubai Women’s Establishment", "مؤسسة دبي للمرأة", "Leadership, empowerment and community development programs.", "برامج القيادة والتمكين والتنمية المجتمعية.", "/img/partners/default-partner.svg"),
            ("Endowment And Minors' Trust Foundation", "مؤسسة الأوقاف وإدارة أموال القصَّر", "Social responsibility, endowment and community values programs.", "برامج المسؤولية المجتمعية والوقف والقيم المجتمعية.", "/img/partners/default-partner.svg"),
            ("General Directorate of Residency and Foreigners Affairs-Dubai", "الإدارة العامة للإقامة وشؤون الأجانب - دبــــــي", "Identity, citizenship services and public awareness programs.", "برامج الهوية وخدمات المتعاملين والتوعية العامة.", "/img/partners/default-partner.svg"),
            ("Hamdan Bin Mohammed Smart University", "جامعة حمدان بن محمد الذكية", "Smart learning, innovation and future skills programs.", "برامج التعلم الذكي والابتكار ومهارات المستقبل.", "/img/partners/default-partner.svg"),
            ("Islamic Affairs and Charitable Activities", "دائرة الشؤون الإسلامية والعمل الخيري", "Values, giving, volunteering and social cohesion programs.", "برامج القيم والعطاء والتطوع والتلاحم المجتمعي.", "/img/partners/default-partner.svg"),
            ("Knowledge and Human Development Authority", "هيئة المعرفة والتنمية البشرية", "Education, wellbeing and lifelong learning programs.", "برامج التعليم والرفاه والتعلم مدى الحياة.", "/img/partners/default-partner.svg"),
            ("Mohammed Bin Rashid Space Centre", "مركز محمد بن راشد للفضاء", "Space science, exploration and future skills learning programs.", "برامج علوم الفضاء والاستكشاف ومهارات المستقبل.", "/img/partners/default-partner.svg"),
            ("Roads and Transport Authority", "هيئة الطرق والمواصلات والمؤسسات التابعة لها", "Mobility, safety, sustainability and public service programs.", "برامج التنقل والسلامة والاستدامة والخدمة العامة.", "/img/partners/default-partner.svg"),
            ("Hamdan Bin Mohammed Heritage Center", "مركز حمدان بن محمد لإحياء التراث", "Heritage, identity and national culture learning programs.", "برامج التراث والهوية والثقافة الوطنية.", "/img/partners/default-partner.svg")
        };

        var clubs = new (string En, string Ar)[]
        {
            ("Shabab Al Ahli Club", "نادي شباب الأهلي"),
            ("Al Nasr Club", "نادي النصر"),
            ("Al Wasl Club", "نادي الوصل"),
            ("Hatta Club", "نادي حتا"),
            ("Dubai Club for People of Determination", "نادي دبي لأصحاب الهمم"),
            ("Dubai Chess & Culture Club", "نادي دبي للشطرنج والثقافة"),
            ("Al Habtoor Polo Club", "نادي الحبتور للبولو")
        };

        var sort = 1;
        foreach (var item in governmentEntities)
        {
            var org = await db.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NameEn == item.En);
            if (org is null)
            {
                org = new Organization
                {
                    OrganizationType = OrganizationType.GovernmentAuthority,
                    NameEn = item.En,
                    NameAr = item.Ar,
                    Email = MakeSeedEmail(item.En),
                    Phone = "0000000000",
                    AddressEn = "Dubai, United Arab Emirates",
                    AddressAr = "دبي، الإمارات العربية المتحدة",
                    Status = ApprovalStatus.Approved,
                    CreatedAtUtc = DateTime.UtcNow,
                    Notes = "Seeded government entity"
                };
                db.Organizations.Add(org);
                await db.SaveChangesAsync();
            }

            org.Status = ApprovalStatus.Approved;
            org.OrganizationType = OrganizationType.GovernmentAuthority;
            org.LogoPath = string.IsNullOrWhiteSpace(org.LogoPath) ? item.Logo : org.LogoPath;
            org.Notes = string.IsNullOrWhiteSpace(org.Notes) ? "Seeded government entity" : org.Notes;

            var profile = await db.PartnerProfiles.FirstOrDefaultAsync(x => x.OrganizationId == org.Id);
            if (profile is null)
            {
                db.PartnerProfiles.Add(new PartnerProfile
                {
                    OrganizationId = org.Id,
                    OverviewEn = item.OverviewEn,
                    OverviewAr = item.OverviewAr,
                    IsFeatured = sort <= 12,
                    FeatureSortOrder = sort,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                profile.OverviewEn = string.IsNullOrWhiteSpace(profile.OverviewEn) ? item.OverviewEn : profile.OverviewEn;
                profile.OverviewAr = string.IsNullOrWhiteSpace(profile.OverviewAr) ? item.OverviewAr : profile.OverviewAr;
                profile.IsFeatured = true;
                profile.FeatureSortOrder = profile.FeatureSortOrder <= 0 ? sort : profile.FeatureSortOrder;
            }
            sort++;
        }

        foreach (var item in clubs)
        {
            if (!await db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.NameEn == item.En))
            {
                db.Organizations.Add(new Organization
                {
                    OrganizationType = OrganizationType.Club,
                    NameEn = item.En,
                    NameAr = item.Ar,
                    Email = MakeSeedEmail(item.En),
                    Phone = "0000000000",
                    AddressEn = "Dubai, United Arab Emirates",
                    AddressAr = "دبي، الإمارات العربية المتحدة",
                    Status = ApprovalStatus.Approved,
                    CreatedAtUtc = DateTime.UtcNow,
                    Notes = "Seeded club"
                });
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seed organizations and partner profiles completed.");
    }

    private static async Task SeedOrgUsersAndLearningProgramsAsync(AppDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        var season = await db.Seasons.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Id).FirstAsync();

        var partnerOrganizations = await db.Organizations
            .Where(x => x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner)
            .OrderBy(x => x.NameEn)
            .ToListAsync();

        foreach (var org in partnerOrganizations)
        {
            var name = org.NameEn;
            if (org is null) continue;

            var email = $"partner-{MakeSlug(name)}@ghars.local";
            var user = await EnsureUserAsync(userManager, email, $"{name} Partner Admin", RoleNames.PartnerAdmin, org.Id, PartnerSeedPassword, logger);
            if (user is null) continue;
            await EnsureOrgLinkAsync(db, org.Id, user.Id, OrganizationType.OtherPartner);
            await SeedProgramsForPartnerAsync(db, season.Id, org, user.Id);
        }

        var clubs = await db.Organizations
            .Where(x => x.OrganizationType == OrganizationType.Club)
            .OrderBy(x => x.NameEn)
            .ToListAsync();

        foreach (var club in clubs)
        {
            var email = $"club-{MakeSlug(club.NameEn)}@ghars.local";
            var user = await EnsureUserAsync(userManager, email, $"{club.NameEn} Club Admin", RoleNames.ClubAdmin, club.Id, ClubSeedPassword, logger);
            if (user is not null) await EnsureOrgLinkAsync(db, club.Id, user.Id, OrganizationType.Club);
        }

        var shabab = clubs.FirstOrDefault(x => x.NameEn == "Shabab Al Ahli Club");
        if (shabab is not null)
        {
            var user = await EnsureUserAsync(userManager, "club1@ghars.local", "Shabab Al Ahli Club Admin", RoleNames.ClubAdmin, shabab.Id, ClubSeedPassword, logger);
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
            "Digital Dubai" => new[]
            {
                ("Ghars values course", "دورة قيم غرس", ActivityType.Course, 0, 35, "A focused course on digital values, responsible technology, and public service culture.", "دورة مركزة حول القيم الرقمية والتقنية المسؤولة وثقافة الخدمة العامة."),
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
        var club = await db.Organizations.FirstOrDefaultAsync(x=>x.OrganizationType==OrganizationType.Club);
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
        var clubs = await db.Organizations.Where(x => x.OrganizationType == OrganizationType.Club).OrderBy(x => x.NameEn).Take(7).ToListAsync();
        var partners = await db.Organizations.Where(x => x.OrganizationType == OrganizationType.GovernmentAuthority || x.OrganizationType == OrganizationType.OtherPartner).OrderBy(x => x.NameEn).Take(8).ToListAsync();
        var adminUser = await userManager.Users.FirstOrDefaultAsync(x => x.Email == "superadmin@ghars.local");
        var adminId = adminUser?.Id ?? "seed";

        // Make sure every selected partner has visible published programs.
        foreach (var partner in partners)
        {
            var partnerUser = await userManager.Users.FirstOrDefaultAsync(x => x.PrimaryOrganizationId == partner.Id);
            if (partnerUser != null) await SeedProgramsForPartnerAsync(db, baseline.Id, partner, partnerUser.Id);
        }
        await db.SaveChangesAsync();

        var activities = await db.Activities.Where(x => x.PartnerOrganizationId != null && x.Status == ActivityStatus.Published).OrderBy(x => x.Id).Take(12).ToListAsync();
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

        // Official (Dubai Digital Authority) survey sample: link only - the analysis report is
        // uploaded by DSC staff after the authority returns its analysis.
        if (!await db.ExternalSurveys.AnyAsync())
        {
            db.ExternalSurveys.Add(new ExternalSurvey
            {
                TitleEn = "Ghars Program Participant Satisfaction Survey",
                TitleAr = "استبيان رضا المشاركين في برنامج غرس",
                DescriptionEn = "Official satisfaction survey developed and analyzed by the Dubai Digital Authority.",
                DescriptionAr = "الاستبيان الرسمي لقياس الرضا، تم تطويره وتحليله من قبل هيئة دبي الرقمية.",
                ExternalUrl = "https://www.digitaldubai.ae",
                SeasonId = baseline.Id,
                IsActive = true,
                IsReportPublished = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var firstClubUser = clubUsers.FirstOrDefault(x => clubs.Any(c => c.Id == x.PrimaryOrganizationId));
        var firstPartnerUser = clubUsers.FirstOrDefault(x => partners.Any(c => c.Id == x.PrimaryOrganizationId));
        await AddNotificationAsync(db, firstPartnerUser?.Id, "New club booking request", "طلب حجز جديد من نادٍ", "A club submitted a booking request awaiting your response.", "قام نادٍ بإرسال طلب حجز بانتظار ردك.", "/PartnerDashboard");
        await AddNotificationAsync(db, firstClubUser?.Id, "Partner proposed modification", "اقترح الشريك تعديلاً", "A partner proposed alternative times for your booking.", "اقترح الشريك أوقاتاً بديلة لحجزك.", "/ClubDashboard");
        await AddNotificationAsync(db, adminUser?.Id, "KPI submitted", "تم إرسال مؤشرات الأداء", "A club KPI submission is ready for review.", "يوجد إرسال مؤشرات أداء من نادٍ بانتظار المراجعة.", "/Admin/Kpi");
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded comprehensive Ghars dummy data for users, clubs, partners, bookings, KPI, agenda, library, gallery and notifications.");
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
