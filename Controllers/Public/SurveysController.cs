using System.Globalization;
using System.Security.Claims;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Public;

/// <summary>
/// The participant side of the Ghars survey engine.
/// </summary>
/// <remarks>
/// Every route here stays inside Ghars. A participant is never sent to another platform to answer the
/// official survey, and no response is ever imported from one.
/// </remarks>
[Authorize]
public class SurveysController : Controller
{
    private readonly AppDbContext _db;

    public SurveysController(AppDbContext db)
    {
        _db = db;
    }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>Cookie name for the per-browser "already answered" guard on anonymous submissions.</summary>
    private static string GuardCookie(int surveyId) => $"ghars_survey_{surveyId}";

    // ---------------------------------------------------------------- landing

    /// <summary>
    /// What a signed-in member can answer right now: the season's official satisfaction survey first,
    /// then any open activity surveys.
    /// </summary>
    [HttpGet("/surveys")]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        var open = await _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Where(x => x.IsActive
                        && (x.OpensAtUtc == null || x.OpensAtUtc <= now)
                        && (x.ClosesAtUtc == null || x.ClosesAtUtc >= now))
            .OrderByDescending(x => x.Purpose)
            .ThenByDescending(x => x.Id)
            .Take(50)
            .ToListAsync();

        ViewBag.OfficialSurveys = open.Where(x => x.Purpose == SurveyPurpose.OfficialSatisfaction).ToList();
        ViewBag.ActivitySurveys = open.Where(x => x.Purpose == SurveyPurpose.General).ToList();
        ViewBag.AnsweredSurveyIds = await _db.SurveyResponses
            .Where(x => x.UserId == userId)
            .Select(x => x.SurveyId)
            .ToListAsync();

        return View();
    }

    // ---------------------------------------------------------------- the shareable link

    /// <summary>
    /// The participant form, reached by the shareable link or its QR code.
    /// </summary>
    /// <remarks>
    /// Anonymous by design: the participants are club players at a lecture, most of whom have no Ghars
    /// account. The token identifies which survey to show and nothing else — it is not a credential,
    /// and the open/closed check below runs on every request regardless of who holds it.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("/surveys/take/{token}")]
    public async Task<IActionResult> Take(string token, int? a = null)
    {
        var survey = await LoadByTokenAsync(token);
        if (survey is null) return NotFound();

        if (!survey.IsOpenAt(DateTime.UtcNow)) return View("Closed", survey);

        if (await AlreadyAnsweredAsync(survey)) return View("AlreadyAnswered", survey);

        ViewBag.AgendaEntry = await ResolveContextAsync(survey, a);
        return View("Take", survey);
    }

    [AllowAnonymous]
    [HttpPost("/surveys/take/{token}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("survey-response")]
    public async Task<IActionResult> Take(string token, int? a, IFormCollection form)
    {
        var survey = await LoadByTokenAsync(token);
        if (survey is null) return NotFound();

        // Re-checked on the POST, not only on the GET. A form left open while DSC closed the survey
        // must not be able to submit into a closed collection.
        if (!survey.IsOpenAt(DateTime.UtcNow)) return View("Closed", survey);

        if (await AlreadyAnsweredAsync(survey)) return View("AlreadyAnswered", survey);

        var context = await ResolveContextAsync(survey, a);
        ViewBag.AgendaEntry = context;

        if (!ValidateRequiredAnswers(survey, form)) return View("Take", survey);

        await SaveResponseAsync(survey, form, context);
        SetGuardCookie(survey);

        return RedirectToAction(nameof(Thanks), new { token });
    }

    [AllowAnonymous]
    [HttpGet("/surveys/take/{token}/thanks")]
    public async Task<IActionResult> Thanks(string token)
    {
        var survey = await _db.Surveys
            .Include(x => x.Season)
            .FirstOrDefaultAsync(x => x.PublicToken == token);
        if (survey is null) return NotFound();
        return View(survey);
    }

    // ---------------------------------------------------------------- club distribution

    /// <summary>
    /// The link and QR code a club hands out after a delivered agenda activity.
    /// </summary>
    /// <remarks>
    /// The club is never named in the URL. The link carries only the agenda entry, and the response
    /// takes its club from that entry's own <see cref="AgendaEntry.OrganizationId"/> — so a club admin
    /// cannot attribute responses to a club by editing a query string. Access to this page is limited
    /// to the agenda entry's owner (DSC excepted), so it also cannot be used to browse other clubs'
    /// agendas.
    /// </remarks>
    [HttpGet("/surveys/distribute/{agendaEntryId:int}")]
    public async Task<IActionResult> Distribute(int agendaEntryId)
    {
        var entry = await _db.AgendaEntries
            .Include(x => x.Organization)
            .Include(x => x.Season)
            .FirstOrDefaultAsync(x => x.Id == agendaEntryId);
        if (entry is null) return NotFound();

        var isDsc = User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.DscAdmin);
        if (!isDsc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var owns = await _db.OrganizationAdminLinks
                .AnyAsync(x => x.UserId == userId && x.OrganizationId == entry.OrganizationId);
            if (!owns) return Forbid();
        }

        var survey = await SatisfactionCalculator.OfficialSurveyAsync(_db, entry.SeasonId);
        if (survey?.PublicToken is null)
        {
            TempData["ToastWarning"] = IsAr()
                ? "لا يوجد استبيان رضا رسمي منشور لهذا الموسم بعد."
                : "No official satisfaction survey has been published for this season yet.";
            return RedirectToAction("Index", "Agenda");
        }

        ViewBag.Survey = survey;
        ViewBag.ShareUrl = $"{Request.Scheme}://{Request.Host}{Url.Content($"~/surveys/take/{survey.PublicToken}")}?a={entry.Id}";
        return View(entry);
    }

    /// <summary>QR code for a club's context-specific distribution link.</summary>
    [HttpGet("/surveys/distribute/{agendaEntryId:int}/qr")]
    public async Task<IActionResult> DistributeQr(int agendaEntryId)
    {
        var entry = await _db.AgendaEntries.FirstOrDefaultAsync(x => x.Id == agendaEntryId);
        if (entry is null) return NotFound();

        var isDsc = User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.DscAdmin);
        if (!isDsc)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var owns = await _db.OrganizationAdminLinks
                .AnyAsync(x => x.UserId == userId && x.OrganizationId == entry.OrganizationId);
            if (!owns) return Forbid();
        }

        var survey = await SatisfactionCalculator.OfficialSurveyAsync(_db, entry.SeasonId);
        if (survey?.PublicToken is null) return NotFound();

        var url = $"{Request.Scheme}://{Request.Host}{Url.Content($"~/surveys/take/{survey.PublicToken}")}?a={entry.Id}";
        return File(QrCodeHelper.GeneratePng(url), "image/png");
    }

    /// <summary>
    /// A club's own aggregate satisfaction results.
    /// </summary>
    /// <remarks>
    /// Scoped to the clubs this user actually administers, resolved from
    /// <see cref="OrganizationAdminLink"/> — a club id in the query string is checked against that
    /// list and refused if it is not on it, so one club cannot read another's results. Aggregates
    /// only: no individual response, no free-text comment and no respondent identity is exposed here,
    /// because a club knowing who said what about its own lecture is precisely what would stop
    /// participants answering honestly. DSC keeps the full view.
    /// </remarks>
    [Authorize(Roles = $"{RoleNames.ClubAdmin},{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
    [HttpGet("/surveys/results")]
    public async Task<IActionResult> ClubResults(int? organizationId, int? seasonId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var isDsc = User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.DscAdmin);

        var myClubs = isDsc
            ? await _db.Organizations.ApprovedClubs().ToListAsync()
            : await _db.OrganizationAdminLinks
                .Include(x => x.Organization)
                .Where(x => x.UserId == userId && x.Organization != null && x.Organization.OrganizationType == OrganizationType.Club)
                .Select(x => x.Organization!)
                .ToListAsync();

        if (myClubs.Count == 0) return Forbid();

        // The requested club must be one of this user's own. Anything else falls back to their first
        // club rather than being honoured.
        var club = organizationId.HasValue
            ? myClubs.FirstOrDefault(x => x.Id == organizationId.Value)
            : myClubs[0];
        if (club is null) return Forbid();

        var seasons = await _db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        var selectedSeasonId = seasonId ?? seasons.FirstOrDefault(x => x.IsActive)?.Id ?? seasons.FirstOrDefault()?.Id;

        ViewBag.MyClubs = myClubs;
        ViewBag.Club = club;
        ViewBag.Seasons = seasons;
        ViewBag.SeasonId = selectedSeasonId;
        ViewBag.MinimumResponses = SatisfactionCalculator.MinimumResponses;

        var survey = selectedSeasonId is null
            ? null
            : await SatisfactionCalculator.OfficialSurveyAsync(_db, selectedSeasonId.Value);
        ViewBag.Survey = survey;

        if (survey is null || selectedSeasonId is null)
        {
            ViewBag.ClubResult = SatisfactionResult.NoData;
            ViewBag.ProgrammeResult = SatisfactionResult.NoData;
            return View("ClubResults");
        }

        ViewBag.ClubResult = await SatisfactionCalculator.FromOfficialSurveyAsync(_db, selectedSeasonId.Value, club.Id);
        // The season figure across every club, so a club can read its own number in context.
        ViewBag.ProgrammeResult = await SatisfactionCalculator.ForSeasonAsync(_db, selectedSeasonId.Value);

        var responses = await _db.SurveyResponses
            .Where(x => x.SurveyId == survey.Id && x.OrganizationId == club.Id)
            .Include(x => x.Answers)
            .ToListAsync();

        var questions = await _db.SurveyQuestions
            .Where(x => x.SurveyId == survey.Id)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var scoped = new Survey
        {
            Id = survey.Id,
            TitleEn = survey.TitleEn,
            TitleAr = survey.TitleAr,
            Purpose = survey.Purpose,
            Questions = questions
        };

        var breakdown = Controllers.Admin.SurveysController.BuildBreakdown(scoped, responses);
        // Free-text answers are individual opinions, not aggregates. They stay with DSC.
        foreach (var row in breakdown) row.Comments.Clear();

        ViewBag.Breakdown = breakdown;
        ViewBag.TotalResponses = responses.Count;
        return View("ClubResults");
    }

    // ---------------------------------------------------------------- legacy activity route

    /// <summary>
    /// The original per-activity route. Kept working so existing links and any survey created before
    /// tokens existed still resolve.
    /// </summary>
    [HttpGet("/surveys/{activityId:int}")]
    public async Task<IActionResult> TakeByActivity(int activityId)
    {
        var survey = await _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.ActivityId == activityId && x.IsActive);
        if (survey is null) return NotFound();

        if (!survey.IsOpenAt(DateTime.UtcNow)) return View("Closed", survey);
        if (await AlreadyAnsweredAsync(survey)) return View("AlreadyAnswered", survey);

        return View("Take", survey);
    }

    [HttpPost("/surveys/{activityId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TakeByActivity(int activityId, IFormCollection form)
    {
        var survey = await _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.ActivityId == activityId && x.IsActive);
        if (survey is null) return NotFound();

        if (!survey.IsOpenAt(DateTime.UtcNow)) return View("Closed", survey);
        if (await AlreadyAnsweredAsync(survey)) return View("AlreadyAnswered", survey);
        if (!ValidateRequiredAnswers(survey, form)) return View("Take", survey);

        await SaveResponseAsync(survey, form, agendaEntry: null);
        SetGuardCookie(survey);

        TempData["ToastSuccess"] = IsAr()
            ? "شكراً لك! تم إرسال ملاحظاتك."
            : "Thank you! Your feedback has been submitted.";
        return RedirectToAction("Index", "Home", new { area = "" });
    }

    // ---------------------------------------------------------------- internals

    private Task<Survey?> LoadByTokenAsync(string token)
        => _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.PublicToken == token);

    /// <summary>
    /// Resolves the club/activity context from an agenda entry id in the link.
    /// </summary>
    /// <remarks>
    /// Returns null rather than refusing when the entry is missing or belongs to another season: a
    /// response with no context is still a valid season-wide response, and losing one is worse than
    /// losing its label. What it never does is take a club id from the request.
    /// </remarks>
    private async Task<AgendaEntry?> ResolveContextAsync(Survey survey, int? agendaEntryId)
    {
        if (agendaEntryId is null) return null;

        var entry = await _db.AgendaEntries
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == agendaEntryId.Value);
        if (entry is null) return null;

        // A response must land in the season it was collected for, or it would move the wrong
        // season's satisfaction figure.
        if (survey.SeasonId.HasValue && entry.SeasonId != survey.SeasonId.Value) return null;

        return entry;
    }

    /// <summary>
    /// Whether this participant has already answered — by account for a signed-in user, by browser
    /// cookie for an anonymous one.
    /// </summary>
    private async Task<bool> AlreadyAnsweredAsync(Survey survey)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
            return await _db.SurveyResponses.AnyAsync(x => x.SurveyId == survey.Id && x.UserId == userId);

        return Request.Cookies.ContainsKey(GuardCookie(survey.Id));
    }

    private void SetGuardCookie(Survey survey)
    {
        Response.Cookies.Append(GuardCookie(survey.Id), "1", new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            // Long enough to cover the session a link is handed out in, short enough that a shared
            // tablet passed around a training hall is usable again the next day.
            Expires = DateTimeOffset.UtcNow.AddHours(12)
        });
    }

    private bool ValidateRequiredAnswers(Survey survey, IFormCollection form)
    {
        foreach (var q in survey.Questions.Where(x => x.IsRequired))
        {
            if (string.IsNullOrWhiteSpace(form[$"q_{q.Id}"]))
            {
                ModelState.AddModelError("", IsAr()
                    ? "يرجى الإجابة على جميع الأسئلة المطلوبة."
                    : "Please answer all required questions.");
                return false;
            }
        }
        return true;
    }

    private async Task SaveResponseAsync(Survey survey, IFormCollection form, AgendaEntry? agendaEntry)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var response = new SurveyResponse
        {
            SurveyId = survey.Id,
            UserId = string.IsNullOrEmpty(userId) ? null : userId,
            AgendaEntryId = agendaEntry?.Id,
            // Server-derived from the agenda entry, never from the request.
            OrganizationId = agendaEntry?.OrganizationId,
            SubmittedAtUtc = DateTime.UtcNow
        };

        _db.SurveyResponses.Add(response);
        await _db.SaveChangesAsync();

        foreach (var q in survey.Questions)
        {
            var raw = form[$"q_{q.Id}"].ToString();
            var ans = new SurveyAnswer { SurveyResponseId = response.Id, SurveyQuestionId = q.Id };

            switch (q.QuestionType)
            {
                case SurveyQuestionType.Stars when byte.TryParse(raw, out var stars) && stars is >= 1 and <= 5:
                    ans.StarsValue = stars;
                    break;

                case SurveyQuestionType.YesNo when bool.TryParse(raw, out var b):
                    ans.BoolValue = b;
                    break;

                case SurveyQuestionType.Text:
                    ans.TextValue = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
                    break;

                case SurveyQuestionType.Mcq when int.TryParse(raw, out var optionId):
                    // The option must belong to this question: a posted id from another question, or
                    // another survey, is discarded rather than stored.
                    if (await _db.SurveyOptions.AnyAsync(o => o.Id == optionId && o.SurveyQuestionId == q.Id))
                        ans.SelectedOptionId = optionId;
                    break;
            }

            _db.SurveyAnswers.Add(ans);
        }

        await _db.SaveChangesAsync();

        // Participation points, unchanged, for signed-in respondents only. An anonymous participant
        // has no wallet to credit.
        if (!string.IsNullOrEmpty(userId))
        {
            await EnsureWalletAsync(userId);
            await AddPointsAsync(userId, 5, "Survey points", "نقاط الاستبيان", PointsReferenceType.Survey, survey.Id);
        }
    }

    private async Task EnsureWalletAsync(string userId)
    {
        var wallet = await _db.UserPointsWallets.FirstOrDefaultAsync(x => x.UserId == userId);
        if (wallet is null)
        {
            _db.UserPointsWallets.Add(new UserPointsWallet
            {
                UserId = userId,
                Balance = 0,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }

    private async Task AddPointsAsync(string userId, int points, string reasonEn, string reasonAr, PointsReferenceType refType, int refId)
    {
        var wallet = await _db.UserPointsWallets.FirstAsync(x => x.UserId == userId);
        wallet.Balance += points;
        wallet.UpdatedAtUtc = DateTime.UtcNow;

        _db.PointsTransactions.Add(new PointsTransaction
        {
            UserId = userId,
            Type = PointsTransactionType.Earn,
            Points = points,
            ReasonEn = reasonEn,
            ReasonAr = reasonAr,
            ReferenceType = refType,
            ReferenceId = refId,
            AtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
