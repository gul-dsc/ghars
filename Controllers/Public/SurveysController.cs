using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GharsPlatform.Controllers.Public;

[Authorize]
public class SurveysController : Controller
{
    private readonly AppDbContext _db;

    public SurveysController(AppDbContext db)
    {
        _db = db;
    }

    // Landing page: the official (Dubai Digital Authority) surveys plus their published analysis
    // reports, alongside the internal Ghars surveys the member can still complete in-platform.
    [HttpGet("/surveys")]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        ViewBag.ExternalSurveys = await _db.ExternalSurveys
            .Include(x => x.Season)
            .Where(x => x.IsActive
                        && (x.StartsAtUtc == null || x.StartsAtUtc <= now)
                        && (x.EndsAtUtc == null || x.EndsAtUtc >= now))
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var internalSurveys = await _db.Surveys
            .Include(x => x.Activity)
            .Where(x => x.IsActive
                        && (x.OpensAtUtc == null || x.OpensAtUtc <= now)
                        && (x.ClosesAtUtc == null || x.ClosesAtUtc >= now))
            .OrderByDescending(x => x.Id)
            .Take(50)
            .ToListAsync();
        var answeredIds = await _db.SurveyResponses
            .Where(x => x.UserId == userId)
            .Select(x => x.SurveyId)
            .ToListAsync();

        ViewBag.InternalSurveys = internalSurveys;
        ViewBag.AnsweredSurveyIds = answeredIds;
        return View();
    }

    [HttpGet("/surveys/{activityId:int}")]
    public async Task<IActionResult> Take(int activityId)
    {
        var survey = await _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.ActivityId == activityId && x.IsActive);

        if (survey is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var already = await _db.SurveyResponses.AnyAsync(x => x.SurveyId == survey.Id && x.UserId == userId);
        if (already)
        {
            TempData["ToastInfo"] = "You already submitted this survey. Thank you!";
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        return View(survey);
    }

    [HttpPost("/surveys/{activityId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Take(int activityId, IFormCollection form)
    {
        var survey = await _db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.ActivityId == activityId && x.IsActive);

        if (survey is null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var already = await _db.SurveyResponses.AnyAsync(x => x.SurveyId == survey.Id && x.UserId == userId);
        if (already)
        {
            TempData["ToastInfo"] = "You already submitted this survey. Thank you!";
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        // Validate required answers
        foreach (var q in survey.Questions)
        {
            var key = $"q_{q.Id}";
            if (q.IsRequired && string.IsNullOrWhiteSpace(form[key]))
            {
                ModelState.AddModelError("", "Please answer all required questions.");
                return View(survey);
            }
        }

        var response = new SurveyResponse
        {
            SurveyId = survey.Id,
            UserId = userId,
            SubmittedAtUtc = DateTime.UtcNow
        };

        _db.SurveyResponses.Add(response);
        await _db.SaveChangesAsync();

        foreach (var q in survey.Questions)
        {
            var key = $"q_{q.Id}";
            var raw = form[key].ToString();

            var ans = new SurveyAnswer
            {
                SurveyResponseId = response.Id,
                SurveyQuestionId = q.Id
            };

            if (q.QuestionType == SurveyQuestionType.Stars && byte.TryParse(raw, out var stars))
                ans.StarsValue = stars;

            else if (q.QuestionType == SurveyQuestionType.YesNo && bool.TryParse(raw, out var b))
                ans.BoolValue = b;

            else if (q.QuestionType == SurveyQuestionType.Text)
                ans.TextValue = raw;

            // Multiple-choice answers reference the selected option row.
            else if (q.QuestionType == SurveyQuestionType.Mcq && int.TryParse(raw, out var optionId))
            {
                var validOption = await _db.SurveyOptions.AnyAsync(o => o.Id == optionId && o.SurveyQuestionId == q.Id);
                if (validOption) ans.SelectedOptionId = optionId;
            }

            _db.SurveyAnswers.Add(ans);
        }

        await _db.SaveChangesAsync();

        // Award points (survey)
        await EnsureWalletAsync(userId);
        await AddPointsAsync(userId, 5, "Survey points", "نقاط الاستبيان", PointsReferenceType.Survey, survey.Id);

        TempData["ToastSuccess"] = "Thank you! Your feedback has been submitted.";
        return RedirectToAction("Index", "Home", new { area = "" });
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
