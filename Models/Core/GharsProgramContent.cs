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
/// <c>docs/About the "Ghars" Program حول برنامج غرس.docx</c>: the introduction, vision, objectives,
/// scope of application and implementation mechanisms.
///
/// The home page, the About page and the Vision &amp; Objectives page all render from here. Public
/// pages must not paraphrase the programme in their own words — a visitor comparing the site with the
/// approved document should find the same sentences, and a change to the document should be a change
/// in one place. This mirrors <see cref="GharsKpiCatalog"/>, which plays the same role for indicators.
/// </summary>
public static class GharsProgramContent
{
    /// <summary>The three introduction paragraphs, in document order.</summary>
    public static readonly IReadOnlyList<BilingualText> Introduction = new[]
    {
        new BilingualText(
            "As part of the implementation of the Dubai Sports Council’s Strategic Plan (2025–2033), and in alignment with the objectives of the Dubai Social Agenda 33, the Council places a strategic priority on developing an integrated and sustainable system for nurturing and advancing sports talents. This approach aims to prepare a generation of athletes capable of achieving excellence, while upholding national values and positive behaviors that reflect the identity of the United Arab Emirates.",
            "في إطار تنفيذ الخطة الاستراتيجية لمجلس دبي الرياضي (2025–2033)، ومواءمة مع مستهدفات أجندة دبي الاجتماعية (33)، يولي المجلس أولوية استراتيجية لتطوير منظومة متكاملة لرعاية وتطوير المواهب الرياضية، بما يسهم في إعداد جيل رياضي قادر على تحقيق التميز، ومتمسك بالقيم الوطنية والسلوكيات الإيجابية، بما يعكس الهوية الوطنية لدولة الإمارات."),
        new BilingualText(
            "In line with these strategic directions, the Dubai Sports Council, in collaboration with Dubai clubs, annually implements the “Ghars” Program as a flagship initiative designed to strengthen the value framework within the sports sector. The program focuses on instilling positive behaviors, enhancing life skills, and fostering comprehensive awareness among players, coaches, and administrators.",
            "وتجسيداً لهذه التوجهات، ينظم مجلس دبي الرياضي بالتعاون مع أندية دبي، برنامج \"غرس\"، باعتباره إحدى المبادرات الاستراتيجية الهادفة إلى تعزيز المنظومة القيمية في القطاع الرياضي، من خلال ترسيخ السلوكيات الإيجابية، وتنمية المهارات الحياتية، وبناء وعي متكامل لدى اللاعبين والفنيين والإداريين."),
        new BilingualText(
            "The program represents a key operational pillar in advancing the objectives of the agenda, including reinforcing national identity, promoting healthy lifestyles, enhancing social cohesion, and providing a safe and enabling sports environment. It contributes to the protection and empowerment of sports talents, ultimately enhancing the quality of outcomes within the sports sector and supporting its sustainable development in the Emirate of Dubai.",
            "ويشكل البرنامج ركيزة تنفيذية لدعم مستهدفات الأجندة في مجالات تعزيز الهوية الوطنية، وترسيخ أنماط الحياة الصحية، وتعزيز التلاحم المجتمعي، وتوفير بيئة رياضية آمنة ومحفزة تسهم في حماية وتمكين المواهب الرياضية، بما ينعكس إيجاباً على جودة مخرجات القطاع الرياضي واستدامة تطوره في إمارة دبي.")
    };

    /// <summary>
    /// The second introduction paragraph, which is the document's own one-sentence statement of what
    /// the programme is. Used as the public lead so the landing page opens with approved wording.
    /// </summary>
    public static readonly BilingualText Summary = Introduction[1];

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
