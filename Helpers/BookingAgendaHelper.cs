using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Helpers;

/// <summary>
/// Creates the Agenda pre-fill for a confirmed booking. Shared by the club and partner booking
/// controllers (previously duplicated). Entries are created as DRAFT: a confirmed booking is a plan,
/// not delivery evidence — the club completes actual data (final date, lecturer, real participant
/// count, media) and submits the entry, which is what dashboards/reports count as delivered.
/// </summary>
public static class BookingAgendaHelper
{
    public static async Task EnsureDraftAgendaEntryAsync(AppDbContext db, BookingRequest booking, string userId)
    {
        var seasonId = booking.SeasonId ?? booking.Activity?.SeasonId;
        if (seasonId is null) return;
        if (await db.AgendaEntries.AnyAsync(x => x.BookingRequestId == booking.Id)) return;

        var subjectEn = !string.IsNullOrWhiteSpace(booking.Subject) ? booking.Subject
            : booking.Activity?.TitleEn ?? "Ghars activity";
        var subjectAr = !string.IsNullOrWhiteSpace(booking.Subject) ? booking.Subject
            : booking.Activity?.TitleAr ?? subjectEn;

        db.AgendaEntries.Add(new AgendaEntry
        {
            SeasonId = seasonId.Value,
            OrganizationId = booking.OrganizationId,
            BookingRequestId = booking.Id,
            ActivityType = booking.RequestedActivityType,
            SubjectEn = subjectEn,
            SubjectAr = subjectAr,
            ActivityDate = booking.ConfirmedStartUtc ?? booking.ProposedStartDateTime ?? booking.Activity?.StartDateTime ?? DateTime.UtcNow,
            Category = MapAgendaCategory(booking.TargetAudienceCsv),
            LecturerName = !string.IsNullOrWhiteSpace(booking.LecturerName) ? booking.LecturerName : "To be confirmed",
            DepartmentOrOrganization = booking.PartnerOrganization?.NameEn ?? booking.Activity?.LocationEn,
            NumberOfParticipants = booking.RequestedSeats,
            Status = AgendaEntryStatus.Draft,
            OtherCategory = booking.OtherTargetAudience,
            Notes = BuildAgendaNotes(booking),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        });
        await db.SaveChangesAsync();
    }

    public static AgendaTargetCategory MapAgendaCategory(string? targetAudienceCsv)
    {
        var values = (targetAudienceCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Contains("Coaches")) return AgendaTargetCategory.Coaches;
        if (values.Contains("Administrators")) return AgendaTargetCategory.Administrators;
        if (values.Contains("Parents")) return AgendaTargetCategory.Parents;
        if (values.Contains("Others")) return AgendaTargetCategory.Others;
        return AgendaTargetCategory.Players;
    }

    private static string BuildAgendaNotes(BookingRequest booking)
    {
        var notes = new List<string> { "Draft created automatically from confirmed Ghars booking " + booking.ReferenceNumber + ". Complete actual delivery data and submit." };
        if (!string.IsNullOrWhiteSpace(booking.AudienceDetails))
            notes.Add($"Audience details: {booking.AudienceDetails}");
        if (!string.IsNullOrWhiteSpace(booking.SpecialRequirements))
            notes.Add($"Special requirements: {booking.SpecialRequirements}");
        if (!string.IsNullOrWhiteSpace(booking.Notes))
            notes.Add($"Club notes: {booking.Notes}");
        return string.Join(Environment.NewLine, notes);
    }
}
