namespace GharsPlatform.Models.Core;

/// <summary>
/// A single piece of approved copy in both languages. <see cref="For"/> falls back to English when the
/// Arabic is missing, so a partially translated entry degrades to readable text rather than a blank.
/// </summary>
public sealed record BilingualText(string En, string Ar)
{
    public string For(bool ar) => ar && !string.IsNullOrWhiteSpace(Ar) ? Ar : En;
}

public sealed record ProgramMechanism(BilingualText Title, BilingualText Body, string Icon);

/// <summary>
/// The approved bilingual description of the Ghars programme, transcribed from the business document
/// <c>docs/About the "Ghars" Program حول برنامج غرس.docx</c>: the vision, objectives, scope of
/// application and implementation mechanisms. The introduction is the Council's later revision; see
/// <see cref="Introduction"/>.
///
/// The home page, the About page and the Vision &amp; Objectives page all render from here. Public
/// pages must not paraphrase the programme in their own words — a visitor comparing the site with the
/// approved document should find the same sentences, and a change to the document should be a change
/// in one place. This mirrors <see cref="GharsKpiCatalog"/>, which plays the same role for indicators.
/// </summary>
public static class GharsProgramContent
{
    /// <summary>The heading the introduction is published under.</summary>
    public static readonly BilingualText IntroductionTitle = new("“Ghars” Program", "برنامج \"غرس\"");

    /// <summary>
    /// The two introduction paragraphs, in order. This is the revised wording the Dubai Sports Council
    /// supplied on 2026-10-04 to replace the three-paragraph introduction from the business document.
    /// The spelling ("behaviours", "organised") is theirs and is kept as supplied. The invisible word
    /// joiner (U+2060) after each en-dash stops a browser breaking the line inside "(2025–2033)", which
    /// it otherwise does in the Arabic hero, leaving "(2025–" at the end of one line.
    /// </summary>
    public static readonly IReadOnlyList<BilingualText> Introduction = new[]
    {
        new BilingualText(
            "The “Ghars” Program contributes to developing a distinguished generation of athletes who embrace national values and positive behaviours, by enhancing life skills and raising awareness among players, coaches, and administrators.",
            "يُسهم برنامج \"غرس\" في إعداد جيل رياضي متميز، متمسك بالقيم الوطنية والسلوكيات الإيجابية، من خلال تنمية المهارات الحياتية وتعزيز الوعي لدى اللاعبين والكوادر الفنية والإدارية."),
        new BilingualText(
            "The program is organised by the Dubai Sports Council in collaboration with Dubai clubs and a group of partners, as part of its Strategic Plan (2025–\u20602033) and in alignment with the Dubai Social Agenda 33. It contributes to creating a safe and supportive sports environment, empowering sports talents, and enhancing the quality and sustainable development of Dubai’s sports sector.",
            "وينظم مجلس دبي الرياضي البرنامج بالتعاون مع أندية دبي ومجموعة من الشركاء، في إطار خطته الاستراتيجية (2025–\u20602033)، ومواءمة مع مستهدفات أجندة دبي الاجتماعية (33)، للإسهام في توفير بيئة رياضية آمنة ومحفزة، وتمكين المواهب الرياضية، وتعزيز جودة واستدامة القطاع الرياضي في إمارة دبي.")
    };

    public static readonly BilingualText Vision = new(
        "To develop a well-rounded and aware generation of athletes, committed to national values and equipped to achieve sporting and societal excellence, thereby reinforcing Dubai’s position as a leading model in sports talent development.",
        "إعداد جيل رياضي واعٍ ومتكامل، متمسك بالقيم الوطنية، ومؤهل لتحقيق التميز الرياضي والمجتمعي، بما يسهم في ترسيخ مكانة دبي نموذجاً رائداً في تنمية المواهب الرياضية.");

    /// <summary>The nine approved programme objectives, in document order.</summary>
    public static readonly IReadOnlyList<BilingualText> Objectives = new[]
    {
        new BilingualText("Instilling national values and Emirati identity among players.", "ترسيخ القيم الوطنية والهوية الإماراتية لدى اللاعبين."),
        new BilingualText("Promoting positive behaviors and building well-balanced athletic personalities.", "تعزيز السلوكيات الإيجابية وبناء شخصية رياضية متوازنة."),
        new BilingualText("Developing players’ life and leadership skills.", "تنمية المهارات الحياتية والقيادية لدى اللاعبين."),
        new BilingualText("Encouraging healthy lifestyles and enhancing health awareness.", "ترسيخ أنماط الحياة الصحية وتعزيز الوعي الصحي."),
        new BilingualText("Protecting and empowering sports talents while reducing negative behaviors.", "حماية وتمكين المواهب الرياضية والحد من السلوكيات السلبية."),
        new BilingualText("Raising community awareness among sports sector stakeholders.", "رفع مستوى الوعي المجتمعي لدى الكوادر الرياضية."),
        new BilingualText("Supporting the ecosystem for identifying and developing talents within Dubai clubs.", "دعم منظومة اكتشاف وتطوير المواهب في أندية دبي."),
        new BilingualText("Contributing to improving the quality of outcomes in the sports sector.", "الإسهام في تحسين جودة مخرجات القطاع الرياضي."),
        new BilingualText("Preparing a generation capable of representing clubs and national teams with distinction.", "إعداد جيل قادر على تمثيل الأندية والمنتخبات الوطنية بصورة مشرفة.")
    };

    public static readonly BilingualText Scope = new(
        "The “Ghars” Program applies to all affiliates of Dubai clubs and private sports academies across the Emirate of Dubai, including players, coaches, administrators, and parents.",
        "يطبق برنامج \"غرس\" على منتسبي أندية دبي والأكاديميات الخاصة في إمارة دبي، ويشمل اللاعبين والمدربين والإداريين وأولياء الأمور.");

    /// <summary>The four audiences named by the scope statement, for use as labels or chips.</summary>
    public static readonly IReadOnlyList<(BilingualText Label, string Icon)> ScopeAudiences = new[]
    {
        (new BilingualText("Players", "اللاعبون"), "bi-person-badge"),
        (new BilingualText("Coaches", "المدربون"), "bi-whistle"),
        (new BilingualText("Administrators", "الإداريون"), "bi-briefcase"),
        (new BilingualText("Parents", "أولياء الأمور"), "bi-people")
    };

    /// <summary>The three approved delivery mechanisms, in document order.</summary>
    public static readonly IReadOnlyList<ProgramMechanism> Mechanisms = new[]
    {
        new ProgramMechanism(
            new BilingualText("Educational Lectures", "المحاضرات التثقيفية"),
            new BilingualText(
                "Delivered through a structured and interactive approach, aimed at enhancing awareness of sports behavior among club members and reinforcing positive values. These sessions are presented through verbal delivery supported by visual aids, contributing to the development of players’ personalities and the creation of a motivating and safe sports environment.",
                "تنفذ وفق منهجية منظمة وبأسلوب تفاعلي، بهدف تعزيز وعي منتسبي الأندية بالسلوك الرياضي وترسيخ القيم الإيجابية، من خلال عروض شفوية مدعمة بوسائل بصرية، بما يسهم في بناء شخصية اللاعب وتوفير بيئة رياضية محفزة وآمنة."),
            "bi-easel"),
        new ProgramMechanism(
            new BilingualText("Digital Awareness", "التوعية عن بعد"),
            new BilingualText(
                "Implemented through the development and dissemination of digital awareness content, including educational videos, interactive platforms, and animated materials tailored for players. This approach aims to promote key sports behavior concepts through simplified, engaging, and accessible formats.",
                "من خلال إنتاج وتقديم محتوى توعوي رقمي، مثل الفيديوهات التثقيفية والروابط التفاعلية والأفلام الكرتونية الموجهة للاعبين، بهدف تعزيز مفاهيم السلوك الرياضي بأساليب مبسطة وجاذبة."),
            "bi-play-btn"),
        new ProgramMechanism(
            new BilingualText("Workshops and Competitions", "ورش العمل والمسابقات"),
            new BilingualText(
                "Designed to develop life and sports skills among youth players, in addition to organizing inter-club competitions that promote positive competitiveness, and reinforce values of collaboration and interaction among clubs.",
                "تهدف إلى تنمية المهارات الحياتية والرياضية لدى اللاعبين، إلى جانب تنظيم مسابقات بين الأندية لتعزيز روح التنافس الإيجابي، وترسيخ قيم التعاون والتفاعل بين مختلف الأندية."),
            "bi-trophy")
    };
}
