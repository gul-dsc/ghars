namespace GharsPlatform.Models.Core;

/// <summary>
/// The organization's own contact details, as shown on the public Contact page.
///
/// These are deliberately configuration-driven and have no defaults. Dubai Sports Council is a real
/// government body, and publishing an invented address or support mailbox for it would be worse than
/// publishing nothing: visitors would write to an address nobody reads. When a value is not
/// configured it is simply not rendered, and the contact form — which routes into this system and
/// therefore always works — carries the page on its own.
///
/// The seeded "Dubai Sports Council" organization row is not a source for these. It is created only
/// by the Development demo seed and carries placeholder values (<c>…@ghars.seed.local</c>,
/// <c>0000000000</c>), so in Production it does not exist at all.
///
/// Keys follow the <c>Ghars:Bootstrap:*</c> convention in <see cref="Data.DbSeeder"/>: a
/// configuration key with a flat environment-variable fallback.
/// </summary>
public sealed record ContactDetails(
    string? Email,
    string? Phone,
    string? AddressEn,
    string? AddressAr,
    string? WebsiteUrl,
    string? OfficeHoursEn,
    string? OfficeHoursAr)
{
    public static readonly ContactDetails None = new(null, null, null, null, null, null, null);

    /// <summary>True when at least one detail is configured, so the view can drop the panel entirely.</summary>
    public bool HasAny =>
        Email is not null || Phone is not null || WebsiteUrl is not null ||
        AddressEn is not null || AddressAr is not null ||
        OfficeHoursEn is not null || OfficeHoursAr is not null;

    public string? Address(bool ar) => (ar ? AddressAr ?? AddressEn : AddressEn ?? AddressAr);

    public string? OfficeHours(bool ar) => (ar ? OfficeHoursAr ?? OfficeHoursEn : OfficeHoursEn ?? OfficeHoursAr);

    public static ContactDetails FromConfiguration(IConfiguration configuration) => new(
        Read(configuration, "Ghars:Contact:Email", "GHARS_CONTACT_EMAIL"),
        Read(configuration, "Ghars:Contact:Phone", "GHARS_CONTACT_PHONE"),
        Read(configuration, "Ghars:Contact:AddressEn", "GHARS_CONTACT_ADDRESS_EN"),
        Read(configuration, "Ghars:Contact:AddressAr", "GHARS_CONTACT_ADDRESS_AR"),
        Read(configuration, "Ghars:Contact:WebsiteUrl", "GHARS_CONTACT_WEBSITE_URL"),
        Read(configuration, "Ghars:Contact:OfficeHoursEn", "GHARS_CONTACT_OFFICE_HOURS_EN"),
        Read(configuration, "Ghars:Contact:OfficeHoursAr", "GHARS_CONTACT_OFFICE_HOURS_AR"));

    private static string? Read(IConfiguration configuration, string configurationKey, string environmentVariable)
    {
        var value = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(value)) value = configuration[environmentVariable];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
