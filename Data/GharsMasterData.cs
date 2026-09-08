using GharsPlatform.Models.Core;

namespace GharsPlatform.Data;

/// <summary>
/// The approved Ghars organizations: the clubs and the implementing entities, with their bilingual
/// names and logo assets.
/// </summary>
/// <remarks>
/// <para>
/// <b>One list, two readers.</b> Development seeding and the reconciliation command both read from
/// here. Keeping the roster in one place is the point — when the seeder and a reconciliation script
/// each carry their own copy, a fresh database and a reconciled one stop agreeing, and nobody notices
/// until a partner appears in one environment and not the other.
/// </para>
/// <para>
/// <b>Provenance.</b> The roster was reconciled from the approved logo asset drops supplied with the
/// project: <c>partners/</c> (17 implementing entities) and <c>clubs/</c> (7 clubs). Those folders are
/// <i>source inputs</i> and are not in source control — their contents were copied into the tracked
/// assets under <c>wwwroot/img/clubs</c> and <c>wwwroot/img/partners</c>, which are the only copies the
/// application reads. This file and those assets are the master data; the drop folders are how it
/// arrived, once.
/// </para>
/// <para>
/// <b>A logo grants nothing.</b> The folders decided <i>which</i> organizations are approved, at
/// reconciliation time, by a person. They are never consulted at runtime, and a filename is never read
/// as authorization. Eligibility at request time is decided entirely by
/// <see cref="Models.Core.Organization.OrganizationType"/> and
/// <see cref="Models.Core.Organization.Status"/> — see <see cref="Helpers.GharsOrganizations"/>.
/// </para>
/// <para>
/// <b>Matching.</b> Rows are matched to existing organizations by <see cref="OrganizationSeed.NameEn"/>,
/// so the names here are the names already in the database, not the logo filenames. Several logos are
/// filed under a fuller legal name than the organization row uses — "General Directorate of Civil
/// Defence Dubai" for "Dubai Civil Defence", "Dubai Culture and Arts Authority" for "Dubai Culture" —
/// and each is deliberately mapped to the existing row rather than inserted a second time.
/// </para>
/// </remarks>
public static class GharsMasterData
{
    public sealed record OrganizationSeed(
        string NameEn,
        string NameAr,
        OrganizationType Type,
        string LogoPath,
        string? OverviewEn = null,
        string? OverviewAr = null);

    /// <summary>Dubai Sports Council: the council that owns the Ghars Program, not an implementing entity.</summary>
    public const string DubaiSportsCouncilNameEn = "Dubai Sports Council";

    public static readonly OrganizationSeed DubaiSportsCouncil = new(
        DubaiSportsCouncilNameEn, "مجلس دبي الرياضي", OrganizationType.DubaiSportsCouncil,
        "/img/brand/dubai-sports-logo.png",
        "Owner and governing body of the Ghars Program.",
        "الجهة المالكة والمشرفة على برنامج غرس.");

    /// <summary>
    /// The approved implementing entities, one per logo in <c>partners/</c>.
    /// </summary>
    public static readonly IReadOnlyList<OrganizationSeed> Partners = new[]
    {
        new OrganizationSeed("Community Development Authority", "هيئة تنمية المجتمع في دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/community-development-authority.png",
            "Community programs and youth development initiatives for Dubai society.",
            "برامج مجتمعية ومبادرات لتنمية الشباب في دبي."),

        new OrganizationSeed("Dubai Civil Defence", "الإدارة العامة للدفاع المدني – دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-civil-defence.png",
            "Safety, emergency readiness and prevention awareness programs.",
            "برامج السلامة والاستعداد للطوارئ والتوعية الوقائية."),

        new OrganizationSeed("Dubai Corporation for Ambulance Services", "مؤسسة دبي لخدمات الإسعاف",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-ambulance.png",
            "First aid, response readiness and community health training.",
            "التدريب على الإسعافات الأولية والجاهزية والاستجابة الصحية المجتمعية."),

        new OrganizationSeed("Dubai Culture", "هيئة الثقافة والفنون في دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-culture.png",
            "Culture, heritage, creativity and identity learning experiences.",
            "تجارب تعليمية في الثقافة والتراث والإبداع والهوية."),

        new OrganizationSeed("Dubai Electricity and Water Authority", "هيئة كهرباء ومياه دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dewa.png",
            "Sustainability, energy, water and climate awareness programs.",
            "برامج الاستدامة والطاقة والمياه والتوعية المناخية."),

        new OrganizationSeed("Dubai Health Authority", "هيئة الصحة في دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dha.png",
            "Wellbeing, preventive health and sports culture learning programs.",
            "برامج الرفاه والصحة الوقائية والثقافة الرياضية."),

        new OrganizationSeed("Dubai Municipality", "بلدية دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-municipality.png",
            "Environment, city services and community responsibility programs.",
            "برامج البيئة وخدمات المدينة والمسؤولية المجتمعية."),

        new OrganizationSeed("Dubai Police", "القيادة العامة لشرطة دبي",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-police.png",
            "Safety, citizenship, prevention and community security awareness.",
            "برامج السلامة والمواطنة والوقاية والأمن المجتمعي."),

        new OrganizationSeed("Dubai Public Library", "مكتبة دبي العامة",
            OrganizationType.GovernmentAuthority, "/img/partners/dubai-public-library.png",
            "Reading, knowledge and cultural literacy programs for young people.",
            "برامج القراءة والمعرفة والثقافة المعرفية للنشء."),

        new OrganizationSeed("Islamic Affairs and Charitable Activities", "دائرة الشؤون الإسلامية والعمل الخيري",
            OrganizationType.GovernmentAuthority, "/img/partners/islamic-affairs-and-charitable-activities.png",
            "Values, giving, volunteering and social cohesion programs.",
            "برامج القيم والعطاء والتطوع والتلاحم المجتمعي."),

        new OrganizationSeed("Ministry of Education", "وزارة التربية والتعليم",
            OrganizationType.GovernmentAuthority, "/img/partners/ministry-of-education.png",
            "Education, school partnership and student development programs.",
            "برامج التعليم والشراكة المدرسية وتطوير الطلبة."),

        new OrganizationSeed("Roads and Transport Authority", "هيئة الطرق والمواصلات",
            OrganizationType.GovernmentAuthority, "/img/partners/rta.png",
            "Mobility, safety, sustainability and public service programs.",
            "برامج التنقل والسلامة والاستدامة والخدمة العامة."),

        // Not government authorities: a private medical centre, two associations, a federal college
        // and a sports federation. Typed OtherPartner, which the eligibility rule treats identically.
        new OrganizationSeed("Al Tadawi Medical Centre", "مركز التداوي الطبي",
            OrganizationType.OtherPartner, "/img/partners/al-tadawi-medical-centre.png",
            "Health screening, sports medicine and wellbeing awareness programs.",
            "برامج الفحص الصحي والطب الرياضي والتوعية بالرفاه."),

        new OrganizationSeed("Emirates Association for Social Development", "جمعية الإمارات للتنمية الاجتماعية",
            OrganizationType.OtherPartner, "/img/partners/emirates-association-for-social-development.png",
            "Social development, family support and community participation programs.",
            "برامج التنمية الاجتماعية ودعم الأسرة والمشاركة المجتمعية."),

        new OrganizationSeed("Emirates Child Protection Association", "جمعية الإمارات لحماية الطفل",
            OrganizationType.OtherPartner, "/img/partners/emirates-child-protection-association.png",
            "Child protection, safeguarding and youth wellbeing awareness programs.",
            "برامج حماية الطفل والوقاية ورفاه النشء."),

        new OrganizationSeed("Higher Colleges of Technology – Dubai Women's College", "كليات التقنية العليا – كلية دبي للطالبات",
            OrganizationType.OtherPartner, "/img/partners/hct-dubai-womens-college.png",
            "Higher education, career readiness and future skills programs.",
            "برامج التعليم العالي والجاهزية المهنية ومهارات المستقبل."),

        new OrganizationSeed("UAE Football Association", "اتحاد الإمارات لكرة القدم",
            OrganizationType.OtherPartner, "/img/partners/uae-football-association.png",
            "Sports ethics, fair play and player development programs.",
            "برامج أخلاقيات الرياضة واللعب النظيف وتطوير اللاعبين.")
    };

    /// <summary>The approved Ghars clubs, one per logo in <c>clubs/</c>.</summary>
    public static readonly IReadOnlyList<OrganizationSeed> Clubs = new[]
    {
        new OrganizationSeed("Shabab Al Ahli Club", "نادي شباب الأهلي",
            OrganizationType.Club, "/img/clubs/shabab-al-ahli-club.png"),
        new OrganizationSeed("Al Nasr Club", "نادي النصر",
            OrganizationType.Club, "/img/clubs/al-nasr-club.png"),
        new OrganizationSeed("Al Wasl Club", "نادي الوصل",
            OrganizationType.Club, "/img/clubs/al-wasl-club.png"),
        new OrganizationSeed("Hatta Club", "نادي حتا",
            OrganizationType.Club, "/img/clubs/hatta-club.png"),
        new OrganizationSeed("Dubai Club for People of Determination", "نادي دبي لأصحاب الهمم",
            OrganizationType.Club, "/img/clubs/dubai-club-for-people-of-determination.png"),
        new OrganizationSeed("Dubai Chess & Culture Club", "نادي دبي للشطرنج والثقافة",
            OrganizationType.Club, "/img/clubs/dubai-chess-and-culture-club.png"),
        new OrganizationSeed("Dubai International Marine Club", "نادي دبي الدولي للرياضات البحرية",
            OrganizationType.Club, "/img/clubs/dubai-international-marine-club.png")
    };

    /// <summary>Every approved organization, clubs and partners and DSC together.</summary>
    public static IEnumerable<OrganizationSeed> All()
        => Clubs.Concat(Partners).Append(DubaiSportsCouncil);

    /// <summary>The approved names, for deciding what an existing database should retain.</summary>
    public static HashSet<string> ApprovedNames()
        => All().Select(x => x.NameEn).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
