using System.Net;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GharsPlatform.Tests.EntityBookingAccessTests;

namespace GharsPlatform.Tests;

/// <summary>
/// Each booking decision is accepted only from the statuses the workflow allows, refused without
/// any change otherwise, and taken exactly once when submitted twice or raced.
/// </summary>
[Collection(AppCollection.Name)]
public class BookingTransitionTests
{
    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public BookingTransitionTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    // ---- the rule table --------------------------------------------------------------------

    public static IEnumerable<object[]> Table()
    {
        var all = BookingStatusText.Distinct;
        var allowed = new Dictionary<BookingDecision, BookingStatus[]>
        {
            [BookingDecision.EntityConfirm] = new[] { BookingStatus.Pending, BookingStatus.ClubRejectedProposedTimes },
            [BookingDecision.EntityReject] = new[] { BookingStatus.Pending, BookingStatus.ClubRejectedProposedTimes },
            [BookingDecision.EntityProposeTimes] = new[] { BookingStatus.Pending, BookingStatus.PartnerProposedNewTime, BookingStatus.ClubRejectedProposedTimes },
            [BookingDecision.ClubAcceptProposedTime] = new[] { BookingStatus.PartnerProposedNewTime },
            [BookingDecision.ClubRejectProposedTimes] = new[] { BookingStatus.PartnerProposedNewTime },
            [BookingDecision.DscApprove] = new[] { BookingStatus.Pending },
            [BookingDecision.DscReject] = new[] { BookingStatus.Pending },
        };
        foreach (var (decision, from) in allowed)
            foreach (var status in all)
                yield return new object[] { decision, status, from.Contains(status) };
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_decision_is_allowed_only_from_its_statuses(BookingDecision decision, BookingStatus status, bool expected)
        => Assert.Equal(expected, BookingWorkflow.IsAllowed(decision, status));

    // ---- through the real endpoints ----------------------------------------------------------

    private async Task<(int Club, int Entity, string ClubUser, string EntityUser)> PartiesAsync()
    {
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var entity = await _data.OrganizationAsync(OrganizationType.OtherPartner);
        return (club, entity,
            await _data.UserAsync(RoleNames.ClubAdmin, false, club),
            await _data.UserAsync(RoleNames.PartnerAdmin, false, entity));
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.PartnerProposedNewTime)]
    public async Task Entity_cannot_confirm_or_reject_a_booking_that_is_not_awaiting_it(BookingStatus status)
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, status, p.ClubUser);
        var before = await _data.BookingRowAsync(id);
        var c = _f.ClientFor(p.EntityUser);

        var confirm = await c.PostAsync("/partner/bookings/approve", Form(("id", id.ToString()), ("lecturerName", "Lecturer")));
        var reject = await c.PostAsync("/partner/bookings/reject", Form(("id", id.ToString()), ("reason", "late")));

        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, reject.StatusCode);
        var after = await _data.BookingRowAsync(id);
        Assert.Equal(status, after.Status);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Null(after.LecturerName);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(0, await _data.Db(db => db.BookingAuditTrails.CountAsync(x => x.BookingRequestId == id)));
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Entity_cannot_propose_times_on_a_finalized_booking(BookingStatus status)
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, status, p.ClubUser);
        var r = await _f.ClientFor(p.EntityUser).PostAsync("/partner/bookings/propose-times", ProposeForm(id));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(status, (await _data.BookingRowAsync(id)).Status);
        Assert.Equal(0, await _data.Db(db => db.BookingProposedTimeOptions.CountAsync(x => x.BookingRequestId == id)));
    }

    [Fact]
    public async Task Entity_confirms_a_pending_booking_once_and_a_repeat_changes_nothing()
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);
        var c = _f.ClientFor(p.EntityUser);

        await c.PostAsync("/partner/bookings/approve", Form(("id", id.ToString()), ("lecturerName", "First")));
        var afterFirst = await _data.BookingRowAsync(id);
        Assert.Equal(BookingStatus.Confirmed, afterFirst.Status);

        await c.PostAsync("/partner/bookings/approve", Form(("id", id.ToString()), ("lecturerName", "Second")));
        var afterSecond = await _data.BookingRowAsync(id);
        Assert.Equal("First", afterSecond.LecturerName);
        Assert.Equal(afterFirst.UpdatedAtUtc, afterSecond.UpdatedAtUtc);
        Assert.Equal(1, await _data.AuditCountAsync(id, "PartnerApproved"));
    }

    [Fact]
    public async Task Racing_confirm_and_reject_produce_exactly_one_decision()
    {
        var p = await PartiesAsync();
        for (var round = 0; round < 5; round++)
        {
            var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);
            var a = _f.ClientFor(p.EntityUser);
            var b = _f.ClientFor(p.EntityUser);

            await Task.WhenAll(
                a.PostAsync("/partner/bookings/approve", Form(("id", id.ToString()), ("lecturerName", "Racer"))),
                b.PostAsync("/partner/bookings/reject", Form(("id", id.ToString()), ("reason", "race"))),
                a.PostAsync("/partner/bookings/approve", Form(("id", id.ToString()), ("lecturerName", "Racer 2"))));

            var decisions = await _data.AuditCountAsync(id, "PartnerApproved") + await _data.AuditCountAsync(id, "PartnerRejected");
            Assert.Equal(1, decisions);
            var row = await _data.BookingRowAsync(id);
            Assert.True(row.Status is BookingStatus.Confirmed or BookingStatus.Rejected);
            Assert.Equal(row.Status == BookingStatus.Confirmed, await _data.AuditCountAsync(id, "PartnerApproved") == 1);
        }
    }

    [Fact]
    public async Task A_stale_claim_changes_nothing()
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);

        using var scopeA = _f.Services.CreateScope();
        using var scopeB = _f.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        var loadedByA = await dbA.BookingRequests.FirstAsync(x => x.Id == id);
        var loadedByB = await dbB.BookingRequests.FirstAsync(x => x.Id == id);

        Assert.Equal(BookingClaimResult.Claimed, await BookingWorkflow.TryClaimAsync(dbB, loadedByB, BookingDecision.EntityReject, p.EntityUser));
        // A still holds the Pending row it loaded earlier.
        Assert.Equal(BookingClaimResult.Stale, await BookingWorkflow.TryClaimAsync(dbA, loadedByA, BookingDecision.EntityConfirm, p.EntityUser));
        Assert.Equal(BookingStatus.Rejected, (await _data.BookingRowAsync(id)).Status);
    }

    [Fact]
    public async Task Club_can_answer_proposed_times_only_while_they_are_proposed()
    {
        var p = await PartiesAsync();
        var pending = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);
        var option = await _data.ProposedOptionAsync(pending, p.EntityUser);
        var c = _f.ClientFor(p.ClubUser);

        await c.PostAsync("/bookings/accept-proposed-time", Form(("bookingId", pending.ToString()), ("optionId", option.ToString())));
        Assert.Equal(BookingStatus.Pending, (await _data.BookingRowAsync(pending)).Status);
        await c.PostAsync("/bookings/reject-proposed-times", Form(("bookingId", pending.ToString())));
        Assert.Equal(BookingStatus.Pending, (await _data.BookingRowAsync(pending)).Status);

        var proposed = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.PartnerProposedNewTime, p.ClubUser);
        var offered = await _data.ProposedOptionAsync(proposed, p.EntityUser);
        await c.PostAsync("/bookings/accept-proposed-time", Form(("bookingId", proposed.ToString()), ("optionId", offered.ToString())));
        var accepted = await _data.BookingRowAsync(proposed);
        Assert.Equal(BookingStatus.Confirmed, accepted.Status);
        Assert.Equal(offered, accepted.AcceptedProposedTimeOptionId);

        // Accepting again, or declining after accepting, changes nothing.
        await c.PostAsync("/bookings/reject-proposed-times", Form(("bookingId", proposed.ToString())));
        Assert.Equal(BookingStatus.Confirmed, (await _data.BookingRowAsync(proposed)).Status);
        Assert.Equal(1, await _data.AuditCountAsync(proposed, "ClubAcceptedProposedTime"));
    }

    [Fact]
    public async Task Entity_may_replace_its_own_pending_proposal()
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);
        var c = _f.ClientFor(p.EntityUser);

        await c.PostAsync("/partner/bookings/propose-times", ProposeForm(id));
        await c.PostAsync("/partner/bookings/propose-times", ProposeForm(id));

        Assert.Equal(BookingStatus.PartnerProposedNewTime, (await _data.BookingRowAsync(id)).Status);
        Assert.Equal(1, await _data.Db(db => db.BookingProposedTimeOptions.CountAsync(x => x.BookingRequestId == id && x.IsActive)));
        Assert.Equal(2, await _data.AuditCountAsync(id, "PartnerProposedNewTime"));
    }

    [Fact]
    public async Task Dsc_decision_is_taken_once_and_notifies_the_club_with_a_page_it_can_open()
    {
        var p = await PartiesAsync();
        var super = await _data.UserAsync(RoleNames.SuperAdmin);
        var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);
        var c = _f.ClientFor(super);

        await Task.WhenAll(
            c.PostAsync($"/Admin/Bookings/Approve/{id}", Form()),
            c.PostAsync($"/Admin/Bookings/Reject/{id}", Form(("reason", "race"))));
        Assert.Equal(1, await _data.AuditCountAsync(id, "Approved") + await _data.AuditCountAsync(id, "Rejected"));

        // The club's notification opens the club booking page, and the club can open it.
        var link = await _data.Db(db => db.Notifications
            .Where(n => n.TargetOrganizationId == p.Club && n.LinkUrl != null && n.LinkUrl.Contains(id.ToString()))
            .Select(n => n.LinkUrl!).FirstAsync());
        Assert.Equal($"/bookings/details/{id}", link);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(p.ClubUser).GetAsync(link)).StatusCode);
        // The page the link used to point at is closed to a club.
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.ClientFor(p.ClubUser).GetAsync($"/Admin/Bookings/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task Notification_links_open_for_their_actual_recipients()
    {
        var p = await PartiesAsync();
        var id = await _data.BookingAsync(p.Club, p.Entity, null, BookingStatus.Pending, p.ClubUser);

        // Entity proposes: the club is notified; the club then declines: the entity is notified.
        await _f.ClientFor(p.EntityUser).PostAsync("/partner/bookings/propose-times", ProposeForm(id));
        await _f.ClientFor(p.ClubUser).PostAsync("/bookings/reject-proposed-times", Form(("bookingId", id.ToString())));

        var deliveries = await _data.Db(db => db.NotificationDeliveries
            .Where(d => d.Notification!.LinkUrl == $"/bookings/details/{id}")
            .Select(d => new { d.UserId, d.Notification!.LinkUrl }).ToListAsync());
        Assert.Contains(deliveries, d => d.UserId == p.ClubUser);
        Assert.Contains(deliveries, d => d.UserId == p.EntityUser);
        foreach (var d in deliveries)
            Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(d.UserId).GetAsync(d.LinkUrl)).StatusCode);
    }
}
