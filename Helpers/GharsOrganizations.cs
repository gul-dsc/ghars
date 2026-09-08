using System.Linq.Expressions;
using GharsPlatform.Models.Core;

namespace GharsPlatform.Helpers;

/// <summary>
/// The single definition of which organizations Ghars treats as eligible implementing entities and
/// as eligible clubs.
/// </summary>
/// <remarks>
/// <para>
/// This rule used to be written out by hand in six controllers. Duplication like that does not stay
/// identical: one copy gains a condition, the others do not, and a screen quietly starts offering
/// something the server will refuse — or worse, accepting something no screen offers. Everything
/// that lists, filters, groups or validates an entity now goes through here.
/// </para>
/// <para>
/// Eligibility is decided by stable database attributes — <see cref="Organization.OrganizationType"/>
/// and <see cref="Organization.Status"/> — never by a logo filename. Logo assets guided which rows
/// were approved during master-data reconciliation; they are presentation only and confer nothing at
/// request time. A partner whose logo file is missing is still a partner; a folder full of images is
/// not an access-control list.
/// </para>
/// </remarks>
public static class GharsOrganizations
{
    /// <summary>
    /// An approved Ghars implementing entity: a government authority or other partner whose
    /// organization record DSC has approved.
    /// </summary>
    /// <remarks>
    /// <see cref="OrganizationType.DubaiSportsCouncil"/> is deliberately absent. DSC owns and governs
    /// the Ghars Program; it is not an entity a club books a lecture from, and listing it among the
    /// implementing entities would invite exactly that.
    /// </remarks>
    public static readonly Expression<Func<Organization, bool>> IsApprovedPartner =
        x => x.Status == ApprovalStatus.Approved
             && (x.OrganizationType == OrganizationType.GovernmentAuthority
                 || x.OrganizationType == OrganizationType.OtherPartner);

    /// <summary>An approved Ghars club.</summary>
    public static readonly Expression<Func<Organization, bool>> IsApprovedClub =
        x => x.Status == ApprovalStatus.Approved
             && x.OrganizationType == OrganizationType.Club;

    /// <summary>
    /// Clubs and academies, the reporting population for KPI, agenda and annual reports. Wider than
    /// <see cref="IsApprovedClub"/> on purpose: those screens have always covered both.
    /// </summary>
    public static readonly Expression<Func<Organization, bool>> IsApprovedClubOrAcademy =
        x => x.Status == ApprovalStatus.Approved
             && (x.OrganizationType == OrganizationType.Club
                 || x.OrganizationType == OrganizationType.PrivateAcademy);

    /// <summary>The approved implementing entities, alphabetically.</summary>
    public static IQueryable<Organization> ApprovedPartners(this IQueryable<Organization> source)
        => source.Where(IsApprovedPartner).OrderBy(x => x.NameEn);

    /// <summary>The approved clubs, alphabetically.</summary>
    public static IQueryable<Organization> ApprovedClubs(this IQueryable<Organization> source)
        => source.Where(IsApprovedClub).OrderBy(x => x.NameEn);

    /// <summary>The approved clubs and academies, alphabetically.</summary>
    public static IQueryable<Organization> ApprovedClubsAndAcademies(this IQueryable<Organization> source)
        => source.Where(IsApprovedClubOrAcademy).OrderBy(x => x.NameEn);

    /// <summary>
    /// Ids of the approved implementing entities, for the places that need a set membership test
    /// rather than a list of rows.
    /// </summary>
    public static IQueryable<int> ApprovedPartnerIds(this IQueryable<Organization> source)
        => source.Where(IsApprovedPartner).Select(x => x.Id);
}
