using GharsPlatform.Models.Core;
using System.Globalization;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// Display labels for <see cref="OrganizationType"/> and <see cref="ApprovalStatus"/>. Labels only:
/// no stored or posted value changes. Both enums carry alias members with the same value
/// (Academy, Partner, PendingPartnerApproval), so option lists are built from <see cref="Types"/>
/// and <see cref="Statuses"/> rather than <c>Enum.GetValues</c>, which would list each twice.
/// </summary>
public static class OrganizationText
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
    private static string T(string en, string ar) => IsAr ? ar : en;

    /// <summary>Every organization type once. Dubai Sports Council is last so callers can drop it.</summary>
    public static readonly IReadOnlyList<OrganizationType> Types = new[]
    {
        OrganizationType.Club,
        OrganizationType.PrivateAcademy,
        OrganizationType.GovernmentAuthority,
        OrganizationType.OtherPartner,
        OrganizationType.DubaiSportsCouncil
    };

    public static readonly IReadOnlyList<ApprovalStatus> Statuses = new[]
    {
        ApprovalStatus.Pending,
        ApprovalStatus.Approved,
        ApprovalStatus.Rejected,
        ApprovalStatus.Suspended
    };

    public static string TypeLabel(OrganizationType type) => type switch
    {
        OrganizationType.Club => T("Club", "نادٍ"),
        OrganizationType.PrivateAcademy => T("Private academy", "أكاديمية خاصة"),
        OrganizationType.GovernmentAuthority => T("Government entity", "جهة حكومية"),
        OrganizationType.OtherPartner => T("Other partner", "جهة شريكة أخرى"),
        OrganizationType.DubaiSportsCouncil => T("Dubai Sports Council", "مجلس دبي الرياضي"),
        _ => type.ToString()
    };

    public static string StatusLabel(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Pending => T("Pending approval", "بانتظار الاعتماد"),
        ApprovalStatus.Approved => T("Approved", "معتمد"),
        ApprovalStatus.Rejected => T("Rejected", "مرفوض"),
        ApprovalStatus.Suspended => T("Suspended", "موقوف"),
        _ => status.ToString()
    };

    public static string StatusBadgeClass(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Approved => "text-bg-success",
        ApprovalStatus.Pending => "text-bg-warning",
        ApprovalStatus.Suspended => "text-bg-secondary",
        _ => "text-bg-danger"
    };

    /// <summary>Display label for an Identity role name. The role name itself stays the stored/posted value.</summary>
    public static string RoleLabel(string? role) => role switch
    {
        "Super Admin" => T("Super Admin", "المشرف العام"),
        "DSC Admin" => T("DSC Admin", "مشرف المجلس"),
        "Club Admin" => T("Club Admin", "مسؤول النادي"),
        "Partner Admin" => T("Partner Admin", "مسؤول الجهة المنفذة"),
        "Academy Admin" => T("Academy Admin", "مسؤول الأكاديمية"),
        "Speaker" => T("Speaker", "متحدث"),
        "Viewer" => T("Viewer", "مشاهد"),
        _ => role ?? ""
    };

    /// <summary>Comma-separated role names (as stored) to display labels.</summary>
    public static string RoleLabels(string? roles) => string.IsNullOrWhiteSpace(roles)
        ? ""
        : string.Join(IsAr ? "، " : ", ", roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(RoleLabel));
}
