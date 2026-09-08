using System.ComponentModel.DataAnnotations;

namespace GharsPlatform.Models.Core;

/// <summary>
/// A supporting document an implementing entity attaches to one of its offerings — a programme
/// outline, a brochure, a trainer profile, a session plan. It is descriptive material about the
/// offering, which is why it hangs off <see cref="Activity"/> rather than off a booking: the same
/// document answers the same question for every club that considers requesting the programme.
///
/// Shaped after <see cref="KpiDocument"/> deliberately, because it is the same kind of record: a
/// row that owns a stored file. <see cref="FilePath"/> holds a <see cref="Helpers.ProtectedFileStore"/>
/// storage key, never a physical server path, so these files sit outside <c>wwwroot</c> and are
/// reachable only through <c>/protected-files/program-attachment/{id}</c>, which re-derives who may
/// read them from the offering's own state. Knowing the key is not a credential.
///
/// There is no bilingual title. The label shown to a reader is the uploaded file's own name, which
/// the partner already controls and already writes in whichever language the document is in;
/// requiring a translated caption for an attachment would be entry work with no business rule behind
/// it. <see cref="OriginalFileName"/> is display metadata only — the file on disk is always a GUID.
/// </summary>
public class ActivityAttachment : AuditableEntity
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Protected-store key, e.g. <c>programs/ab12….pdf</c>.</summary>
    [Required, MaxLength(500)]
    public string FilePath { get; set; } = "";

    /// <summary>The name the file arrived with, used as its label and download name. Never used on disk.</summary>
    [MaxLength(250)]
    public string? OriginalFileName { get; set; }

    /// <summary>Size at upload, so a list can show it without touching the file system per row.</summary>
    public long FileSizeBytes { get; set; }
}
