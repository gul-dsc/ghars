using System.Net;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using GharsPlatform.Tests.Infrastructure;

namespace GharsPlatform.Tests;

/// <summary>
/// One organization rule for the entity side of a booking: the dashboard list, the details page and
/// Confirm / Reject / Propose all admit a user only when the booking's implementing organization is
/// one they are linked to now. Program authorship grants nothing.
/// </summary>
[Collection(AppCollection.Name)]
public class EntityBookingAccessTests
{
    private readonly GharsAppFactory _f;
    private readonly TestData _data;

    public EntityBookingAccessTests(GharsAppFactory f)
    {
        _f = f;
        _data = new TestData(f);
    }

    private sealed record World(
        int Club, int E1, int E2, string ClubUser, string E1User, string E2User, string MultiUser, string FormerUser,
        string UnlinkedPartner, string Creator, int OwnBooking, int LegacyBooking, int OtherBooking, int CreatorsProgramBooking);

    private async Task<World> BuildAsync()
    {
        var club = await _data.OrganizationAsync(OrganizationType.Club);
        var e1 = await _data.OrganizationAsync(OrganizationType.OtherPartner);
        var e2 = await _data.OrganizationAsync(OrganizationType.GovernmentAuthority);

        var clubUser = await _data.UserAsync(RoleNames.ClubAdmin, false, club);
        var e1User = await _data.UserAsync(RoleNames.PartnerAdmin, false, e1);
        var e2User = await _data.UserAsync(RoleNames.PartnerAdmin, false, e2);
        var multi = await _data.UserAsync(RoleNames.PartnerAdmin, false, e1, e2);
        var former = await _data.UserAsync(RoleNames.PartnerAdmin, false, e1);
        await _data.UnlinkAsync(former, e1);
        var unlinked = await _data.UserAsync(RoleNames.PartnerAdmin);
        // Works for E2, but typed in a program that belongs to E1.
        var creator = await _data.UserAsync(RoleNames.PartnerAdmin, false, e2);

        var e1Program = await _data.ActivityAsync(e1, e1User);
        var e1ProgramByCreator = await _data.ActivityAsync(e1, creator);
        var e2Program = await _data.ActivityAsync(e2, e2User);

        var own = await _data.BookingAsync(club, e1, e1Program, BookingStatus.Pending, clubUser);
        // An older booking that never recorded its entity: it belongs to its program's entity.
        var legacy = await _data.BookingAsync(club, null, e1Program, BookingStatus.Pending, clubUser);
        var other = await _data.BookingAsync(club, e2, e2Program, BookingStatus.Pending, clubUser);
        var creatorsProgram = await _data.BookingAsync(club, e1, e1ProgramByCreator, BookingStatus.Pending, clubUser);

        return new World(club, e1, e2, clubUser, e1User, e2User, multi, former, unlinked, creator, own, legacy, other, creatorsProgram);
    }

    private async Task<HttpStatusCode> DetailsAsync(string userId, int bookingId)
        => (await _f.ClientFor(userId).GetAsync($"/bookings/details/{bookingId}")).StatusCode;

    private async Task<string> DashboardAsync(string userId)
    {
        var r = await _f.ClientFor(userId).GetAsync("/partner");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    private async Task<bool> ListedAsync(string html, int bookingId)
        => html.Contains(TestData.Reference(await _data.BookingRowAsync(bookingId)));

    [Fact]
    public async Task Entity_user_sees_and_opens_exactly_its_organizations_bookings()
    {
        var w = await BuildAsync();
        var html = await DashboardAsync(w.E1User);

        Assert.True(await ListedAsync(html, w.OwnBooking));
        Assert.True(await ListedAsync(html, w.LegacyBooking));
        Assert.True(await ListedAsync(html, w.CreatorsProgramBooking));
        Assert.False(await ListedAsync(html, w.OtherBooking));

        // Everything the dashboard lists also opens.
        foreach (var id in new[] { w.OwnBooking, w.LegacyBooking, w.CreatorsProgramBooking })
            Assert.Equal(HttpStatusCode.OK, await DetailsAsync(w.E1User, id));
        Assert.Equal(HttpStatusCode.NotFound, await DetailsAsync(w.E1User, w.OtherBooking));
    }

    [Fact]
    public async Task Creating_a_program_grants_no_access_to_another_entitys_bookings()
    {
        var w = await BuildAsync();

        Assert.False(await ListedAsync(await DashboardAsync(w.Creator), w.CreatorsProgramBooking));
        Assert.Equal(HttpStatusCode.NotFound, await DetailsAsync(w.Creator, w.CreatorsProgramBooking));

        var confirm = await _f.ClientFor(w.Creator).PostAsync("/partner/bookings/approve", Form(("id", w.CreatorsProgramBooking.ToString()), ("lecturerName", "Someone")));
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Equal(BookingStatus.Pending, (await _data.BookingRowAsync(w.CreatorsProgramBooking)).Status);
    }

    [Fact]
    public async Task User_with_two_memberships_reaches_both_and_nothing_else()
    {
        var w = await BuildAsync();
        var html = await DashboardAsync(w.MultiUser);
        Assert.True(await ListedAsync(html, w.OwnBooking));
        Assert.True(await ListedAsync(html, w.OtherBooking));
        Assert.Equal(HttpStatusCode.OK, await DetailsAsync(w.MultiUser, w.OwnBooking));
        Assert.Equal(HttpStatusCode.OK, await DetailsAsync(w.MultiUser, w.OtherBooking));
    }

    [Fact]
    public async Task Removed_membership_and_no_membership_see_nothing()
    {
        var w = await BuildAsync();

        foreach (var user in new[] { w.FormerUser, w.UnlinkedPartner })
        {
            var html = await DashboardAsync(user);
            // Previously an account with no entity link got the whole platform's bookings here.
            Assert.False(await ListedAsync(html, w.OwnBooking));
            Assert.False(await ListedAsync(html, w.OtherBooking));
            Assert.Equal(HttpStatusCode.NotFound, await DetailsAsync(user, w.OwnBooking));

            var reject = await _f.ClientFor(user).PostAsync("/partner/bookings/reject", Form(("id", w.OwnBooking.ToString())));
            Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
            var propose = await _f.ClientFor(user).PostAsync("/partner/bookings/propose-times", ProposeForm(w.OwnBooking));
            Assert.Equal(HttpStatusCode.NotFound, propose.StatusCode);
        }
        Assert.Equal(BookingStatus.Pending, (await _data.BookingRowAsync(w.OwnBooking)).Status);
    }

    [Fact]
    public async Task Other_entity_cannot_confirm_reject_or_propose()
    {
        var w = await BuildAsync();
        var c = _f.ClientFor(w.E2User);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/partner/bookings/approve", Form(("id", w.OwnBooking.ToString()), ("lecturerName", "X")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/partner/bookings/reject", Form(("id", w.OwnBooking.ToString())))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/partner/bookings/propose-times", ProposeForm(w.OwnBooking))).StatusCode);
        Assert.Equal(BookingStatus.Pending, (await _data.BookingRowAsync(w.OwnBooking)).Status);
    }

    [Fact]
    public async Task Legacy_booking_without_recorded_entity_is_decided_by_its_programs_entity()
    {
        var w = await BuildAsync();
        var r = await _f.ClientFor(w.E1User).PostAsync("/partner/bookings/approve", Form(("id", w.LegacyBooking.ToString()), ("lecturerName", "Lecturer")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var row = await _data.BookingRowAsync(w.LegacyBooking);
        Assert.Equal(BookingStatus.Confirmed, row.Status);
        Assert.Equal(w.E1, row.PartnerOrganizationId); // recorded from the program, not from the user's first link
    }

    [Fact]
    public async Task Dsc_admin_keeps_access_and_club_keeps_its_own_booking()
    {
        var w = await BuildAsync();
        var dsc = await _data.UserAsync(RoleNames.DscAdmin);
        Assert.Equal(HttpStatusCode.OK, (await _f.ClientFor(dsc).GetAsync($"/Admin/Bookings/Details/{w.OwnBooking}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await DetailsAsync(w.ClubUser, w.OwnBooking));
        Assert.Equal(HttpStatusCode.OK, await DetailsAsync(w.ClubUser, w.OtherBooking));
    }

    internal static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    internal static FormUrlEncodedContent ProposeForm(int bookingId)
    {
        var start = DateTime.UtcNow.AddDays(40);
        return Form(("bookingId", bookingId.ToString()),
            ("starts", start.ToString("yyyy-MM-ddTHH:mm")), ("ends", start.AddHours(2).ToString("yyyy-MM-ddTHH:mm")));
    }
}
