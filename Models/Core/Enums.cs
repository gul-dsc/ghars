namespace GharsPlatform.Models.Core;

public enum OrganizationType : byte
{
    Club = 1,
    PrivateAcademy = 2,
    GovernmentAuthority = 3,
    OtherPartner = 4,

    /// <summary>
    /// Dubai Sports Council itself — the council that owns and governs the Ghars Program.
    /// </summary>
    /// <remarks>
    /// Its own type rather than a government authority, because every partner query in the platform
    /// asks "which entities can a club book a lecture from?" and the answer must not include the
    /// council running the programme. Separating it keeps DSC's organization record, users and
    /// history intact while removing it from every implementing-entity selector, with no filter
    /// anywhere needing to name it. See <see cref="Helpers.GharsOrganizations"/>.
    /// </remarks>
    DubaiSportsCouncil = 5,

    // Legacy aliases kept for backward compatibility in code paths
    Academy = PrivateAcademy,
    Partner = OtherPartner
}

public enum ApprovalStatus : byte
{
    Pending = 1,
    PendingPartnerApproval = 1,
    Approved = 2,
    Rejected = 3,
    Suspended = 4
}

public enum ActivityType : byte
{
    Lecture = 1,
    Workshop = 2,
    TrainingProgram = 3,
    Course = 4,
    Event = 5,
    Activity = 10
}

public enum ActivityStatus : byte
{
    Draft = 1,
    Published = 2,
    Closed = 3,
    Cancelled = 4
}

/// <summary>
/// The DSC review lifecycle for a partner-created offering. Deliberately a separate enum from
/// <see cref="ActivityStatus"/> rather than extra members on it: <c>Status == Published</c> is
/// compared in a dozen unrelated places (surveys, attendance, the public home page, the admin
/// dashboard) and widening that enum would change what those comparisons silently exclude.
///
/// The column is null for DSC-created activities, which have no owning entity and are outside this
/// workflow entirely.
///
/// Visibility to clubs requires BOTH <see cref="Approved"/> here and
/// <see cref="ActivityStatus.Published"/> there; only a DSC approval ever sets the pair.
/// </summary>
public enum OfferingApprovalStatus : byte
{
    Draft = 1,
    SubmittedForApproval = 2,
    ReturnedForCorrection = 3,
    Approved = 4,
    Rejected = 5,
    Unpublished = 6
}

public enum BookingStatus : byte
{
    Pending = 1,
    PendingPartnerApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    PartnerProposedNewTime = 5,
    Confirmed = 6,
    ClubRejectedProposedTimes = 7
}

public enum AttendanceMethod : byte
{
    Manual = 1,
    Qr = 2,
    Import = 3
}

public enum CertificateStatus : byte
{
    Issued = 1,
    Revoked = 2
}

public enum SurveyQuestionType : byte
{
    Stars = 1,
    YesNo = 2,
    Text = 3,
    Mcq = 4
}

/// <summary>
/// What a survey is *for*, as a stored attribute rather than something inferred from its title.
/// </summary>
/// <remarks>
/// The official participant satisfaction survey feeds the Ghars satisfaction KPI, so the platform has
/// to be able to recognise it with certainty. Matching on title text would make the KPI depend on a
/// string an administrator can edit at any moment, in either language.
/// </remarks>
public enum SurveyPurpose : byte
{
    /// <summary>An ordinary Ghars survey attached to a single activity. Never feeds the KPI.</summary>
    General = 1,

    /// <summary>
    /// The official Ghars Program Participant Satisfaction Survey for one sports season. At most one
    /// per season, enforced by a filtered unique index.
    /// </summary>
    OfficialSatisfaction = 2
}

public enum MediaType : byte
{
    Image = 1,
    Video = 2,
    Pdf = 3
}

public enum PointsTransactionType : byte
{
    Earn = 1,
    Spend = 2,
    AdminAdjust = 3
}

public enum PointsReferenceType : byte
{
    LibraryDownload = 1,
    Attendance = 2,
    Survey = 3,
    RewardRedemption = 4,
    Other = 10
}

public enum RedemptionStatus : byte
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,
    Delivered = 4
}

public enum NotificationType : byte
{
    Info = 1,
    Success = 2,
    Warning = 3,
    Danger = 4,
    System = 10
}

public enum NotificationTargetType : byte
{
    All = 1,
    Role = 2,
    Organization = 3,
    User = 4
}

public enum CalendarEventType : byte
{
    Lecture = 1,
    Activity = 2,
    Booking = 3,
    Other = 10
}

public enum OrganizationDocumentType : byte
{
    License = 1,
    ApprovalLetter = 2,
    Mou = 3,
    Other = 10
}


public enum LibraryContentType : byte
{
    Lecture = 1,
    AwarenessVideo = 2,
    EducationalBooklet = 3
}

public enum AgendaEntryStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4
}

public enum AgendaTargetCategory : byte
{
    Players = 1,
    Coaches = 2,
    Administrators = 3,
    Parents = 4,
    Others = 10
}

public enum KpiSubmissionStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    MoreInfoRequired = 5
}

/// <summary>
/// The DSC review lifecycle for a club's Ghars Annual Report. Deliberately its own enum rather than a
/// reuse of <see cref="KpiSubmissionStatus"/>: the two records are compared and filtered independently
/// across dashboards and review queues, and an Annual Report freezes on approval while a KPI
/// submission does not. The numeric layout is kept identical so the two read the same way in the
/// database, and the returned state is named for what the requirement calls it.
/// </summary>
public enum AnnualReportStatus : byte
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    ReturnedForCorrection = 5
}

/// <summary>
/// What a Ghars Channel item is, for the channel's content filters. Distinct from
/// <see cref="GalleryMediaType"/>, which says what the file *is* (photo/video); this says what it is
/// *for*. Nullable on the row: every item that predates the channel is classified by fallback from
/// its existing fields rather than being back-filled by guesswork.
/// </summary>
public enum ChannelCategory : byte
{
    ClubActivity = 1,
    Awareness = 2,
    Educational = 3,
    Official = 4,
    Press = 5
}

/// <summary>
/// The DSC review lifecycle for partner/government content submitted to the Ghars Channel.
///
/// The column is <c>null</c> for club activity media and DSC uploads, which are outside this workflow
/// entirely — exactly the convention <see cref="OfferingApprovalStatus"/> uses for DSC-created
/// activities. Visibility requires BOTH <see cref="Approved"/> here and <c>IsPublished</c> on the row;
/// only a DSC approval ever sets the pair.
/// </summary>
public enum ChannelApprovalStatus : byte
{
    Draft = 1,
    SubmittedForApproval = 2,
    ReturnedForCorrection = 3,
    Approved = 4,
    Rejected = 5,
    Unpublished = 6
}

public enum GalleryMediaType : byte
{
    Photo = 1,
    Video = 2,
    PressCoverage = 3,
    NewspaperCoverage = 4,
    OfficialPhoto = 5,
    OfficialVideo = 6
}

/// <summary>
/// Subject of a public contact enquiry. Determines nothing but routing/filtering — every enquiry
/// reaches the same DSC reviewers, because there is no per-topic mailbox in this system.
/// </summary>
public enum ContactTopic : byte
{
    GeneralEnquiry = 1,
    LectureOrEventBooking = 2,
    ClubOrAcademyRegistration = 3,
    PartnershipOrImplementingEntity = 4,
    KpiOrReporting = 5,
    TechnicalSupport = 6,
    Other = 10
}

public enum ContactMessageStatus : byte
{
    New = 1,
    InProgress = 2,
    Resolved = 3,
    Spam = 4
}
