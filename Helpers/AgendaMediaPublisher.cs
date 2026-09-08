using GharsPlatform.Data;
using GharsPlatform.Models.Core;

namespace GharsPlatform.Helpers;

/// <summary>
/// The one way club activity media enters the Ghars Channel.
///
/// A club's photo or video is stored once and referenced by two rows: an <see cref="AgendaMedia"/> row,
/// which keeps it attached to the club's delivery record, and a <see cref="GalleryItem"/> row, which is
/// what the channel reads. There is no second copy of the file.
///
/// Both club entry points — the Agenda entry form and "contribute media" in the channel — call this, so
/// they cannot drift into producing differently-shaped rows. Club media carries
/// <c>ApprovalStatus == null</c>: it is outside the partner review workflow and is published on
/// arrival, exactly as it always has been. <c>IsPublished == false</c> continues to mean a DSC takedown.
/// </summary>
public static class AgendaMediaPublisher
{
    /// <summary>Club activity media stays where it has always been stored, which Program.cs denies to the
    /// static file middleware so publication is decided per row by the serving endpoint.</summary>
    public const string UploadFolder = "uploads/agenda";

    /// <summary>
    /// Saves validated uploads against a delivered agenda entry and returns how many were stored.
    /// The caller is responsible for having validated the files and for having proved that
    /// <paramref name="entry"/> belongs to the current user's club.
    /// </summary>
    public static async Task<int> PublishAsync(
        AppDbContext db,
        IWebHostEnvironment env,
        AgendaEntry entry,
        IEnumerable<IFormFile>? files,
        string? userId,
        string? captionEn = null,
        string? captionAr = null)
    {
        if (files is null) return 0;

        var stored = 0;
        foreach (var file in files.Where(f => f.Length > 0))
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var path = await FileValidationHelper.SaveAsync(file, env.WebRootPath, UploadFolder);
            var mediaType = ext is ".mp4" or ".webm" or ".mov" ? GalleryMediaType.Video : GalleryMediaType.Photo;

            db.AgendaMedia.Add(new AgendaMedia
            {
                AgendaEntryId = entry.Id,
                MediaType = mediaType,
                FilePath = path,
                TitleEn = entry.SubjectEn,
                TitleAr = entry.SubjectAr,
                IsPublished = true
            });

            db.GalleryItems.Add(new GalleryItem
            {
                TitleEn = entry.SubjectEn,
                TitleAr = entry.SubjectAr,
                DescriptionEn = string.IsNullOrWhiteSpace(captionEn) ? entry.Notes : captionEn,
                DescriptionAr = string.IsNullOrWhiteSpace(captionAr) ? entry.Notes : captionAr,
                MediaType = mediaType,
                ChannelCategory = ChannelCategory.ClubActivity,
                FilePath = path,
                OrganizationId = entry.OrganizationId,
                AgendaEntryId = entry.Id,
                SeasonId = entry.SeasonId,
                MediaDate = entry.ActivityDate,
                // Club media is published on arrival and is not part of the partner approval workflow.
                IsPublished = true,
                ApprovalStatus = null,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = userId
            });

            stored++;
        }

        if (stored > 0) await db.SaveChangesAsync();
        return stored;
    }
}
