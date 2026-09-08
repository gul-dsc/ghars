using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

public class GalleryItem : AuditableEntity
{
    public int Id { get; set; }
    [Required, MaxLength(250)] public string TitleEn { get; set; } = "";
    [Required, MaxLength(250)] public string TitleAr { get; set; } = "";
    [MaxLength(2000)] public string? DescriptionEn { get; set; }
    [MaxLength(2000)] public string? DescriptionAr { get; set; }
    public GalleryMediaType MediaType { get; set; } = GalleryMediaType.Photo;
    [MaxLength(500)] public string? FilePath { get; set; }
    [MaxLength(700)] public string? ExternalUrl { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? ActivityId { get; set; }
    public Activity? Activity { get; set; }
    public int? AgendaEntryId { get; set; }
    public AgendaEntry? AgendaEntry { get; set; }
    public int SeasonId { get; set; }
    public Season? Season { get; set; }
    public DateTime MediaDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// DSC takedown flag, not a draft flag: club media created through the Agenda is published on
    /// arrival and only ever goes false through an admin Hide. Partner content added to the Ghars
    /// Channel is the one exception — it starts false and is set true by a DSC approval, so
    /// publication still means the same thing everywhere: "DSC is content for this to be visible".
    /// </summary>
    public bool IsPublished { get; set; } = true;

    // ------------------------------------------------------------------ Ghars Channel
    //
    // The channel is this table, extended rather than forked: agenda media already lands here and both
    // the public and admin gallery already read it. Every column below is nullable so all pre-existing
    // rows stay valid and nothing has to be back-filled.

    /// <summary>
    /// What the item is *for*, as opposed to what the file is. Null on rows that predate the channel;
    /// <see cref="Helpers.ChannelWorkflow.ResolveCategory"/> classifies those from their existing
    /// fields rather than back-filling a guess into the database.
    /// </summary>
    public ChannelCategory? ChannelCategory { get; set; }

    /// <summary>
    /// Null for club activity media and DSC uploads — they are outside the partner review workflow.
    /// Set only on content an implementing entity contributed to the channel.
    /// </summary>
    public ChannelApprovalStatus? ApprovalStatus { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    [MaxLength(450)] public string? SubmittedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }

    /// <summary>The reviewer's note from the most recent return or rejection. Cleared on approval.</summary>
    [MaxLength(2000)] public string? ReviewNotes { get; set; }

    /// <summary>
    /// Optional pointer to a Digital Library publication instead of a duplicate upload. The channel
    /// deliberately accepts no documents (see the design note §2.4); a partner whose material is a
    /// booklet links to the library copy so the same file is never stored twice.
    /// </summary>
    public int? LibraryItemId { get; set; }
    public LibraryItem? LibraryItem { get; set; }
}
