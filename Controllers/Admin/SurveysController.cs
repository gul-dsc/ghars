using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using GharsPlatform.Data;
using GharsPlatform.Helpers;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

/// <summary>
/// DSC survey administration for the one native Ghars survey engine — both ordinary activity surveys
/// and the official participant satisfaction survey.
/// </summary>
/// <remarks>
/// Authoring is DSC-only. Clubs distribute the official survey and read their own results; they never
/// define it, because a satisfaction instrument each club could reword is not a comparable instrument.
/// </remarks>
[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class SurveysController : Controllers.BaseController
{
    public SurveysController(AppDbContext db) : base(db) { }

    private static bool IsAr() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    // ---------------------------------------------------------------- listing

    public async Task<IActionResult> Index()
    {
        var all = await Db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Include(x => x.Questions)
            .OrderByDescending(x => x.Purpose)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var counts = await Db.SurveyResponses
            .GroupBy(x => x.SurveyId)
            .Select(g => new { SurveyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SurveyId, x => x.Count);

        ViewBag.ResponseCounts = counts;
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
        return View(all);
    }

    // ---------------------------------------------------------------- create

    public async Task<IActionResult> Create(SurveyPurpose purpose = SurveyPurpose.General)
    {
        await LoadLookupsAsync();
        var activeSeason = await Db.Seasons.Where(x => x.IsActive).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();

        var vm = new SurveyVm { Purpose = purpose, IsActive = true };
        if (purpose == SurveyPurpose.OfficialSatisfaction)
        {
            vm.SeasonId = activeSeason?.Id;
            vm.TitleEn = OfficialSurveyTemplate.TitleEn;
            vm.TitleAr = OfficialSurveyTemplate.TitleAr;
            vm.DescriptionEn = OfficialSurveyTemplate.DescriptionEn;
            vm.DescriptionAr = OfficialSurveyTemplate.DescriptionAr;
        }
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SurveyVm vm)
    {
        await LoadLookupsAsync();
        await ValidateAsync(vm, existingId: null);
        if (!ModelState.IsValid) return View(vm);

        var official = vm.Purpose == SurveyPurpose.OfficialSatisfaction;

        var s = new Survey
        {
            Purpose = vm.Purpose,
            // An official survey belongs to a season, an activity survey to an activity. Storing the
            // other anchor as well would leave two contradictory answers to "what is this about".
            ActivityId = official ? null : vm.ActivityId,
            SeasonId = official ? vm.SeasonId : null,
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            DescriptionEn = vm.DescriptionEn?.Trim(),
            DescriptionAr = vm.DescriptionAr?.Trim(),
            IsActive = vm.IsActive,
            OpensAtUtc = vm.OpensAtUtc,
            ClosesAtUtc = vm.ClosesAtUtc,
            PublicToken = NewToken(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.Surveys.Add(s);
        await Db.SaveChangesAsync();

        Db.SurveyQuestions.AddRange(official
            ? OfficialSurveyTemplate.Questions(s.Id)
            : OfficialSurveyTemplate.ActivityQuestions(s.Id));
        await Db.SaveChangesAsync();

        await AuditAsync("Create", nameof(Survey), s.Id.ToString(), null, new { s.TitleEn, s.Purpose, s.SeasonId, s.ActivityId });

        TempData["ToastSuccess"] = IsAr()
            ? "تم إنشاء الاستبيان مع الأسئلة الافتراضية."
            : "Survey created with its default questions.";
        return RedirectToAction(nameof(Questions), new { id = s.Id });
    }

    // ---------------------------------------------------------------- edit

    public async Task<IActionResult> Edit(int id)
    {
        var s = await Db.Surveys.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        await LoadLookupsAsync();

        return View(new SurveyVm
        {
            Id = s.Id,
            Purpose = s.Purpose,
            ActivityId = s.ActivityId,
            SeasonId = s.SeasonId,
            TitleEn = s.TitleEn,
            TitleAr = s.TitleAr,
            DescriptionEn = s.DescriptionEn,
            DescriptionAr = s.DescriptionAr,
            IsActive = s.IsActive,
            OpensAtUtc = s.OpensAtUtc,
            ClosesAtUtc = s.ClosesAtUtc
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SurveyVm vm)
    {
        var s = await Db.Surveys.FirstOrDefaultAsync(x => x.Id == vm.Id);
        if (s is null) return NotFound();

        await LoadLookupsAsync();
        // The purpose of an existing survey is fixed. Re-pointing a general survey at the official
        // slot mid-season would silently change which responses the season's KPI is computed from.
        vm.Purpose = s.Purpose;
        await ValidateAsync(vm, existingId: s.Id);
        if (!ModelState.IsValid) return View(vm);

        var old = new { s.TitleEn, s.SeasonId, s.ActivityId, s.IsActive, s.OpensAtUtc, s.ClosesAtUtc };

        s.TitleEn = vm.TitleEn.Trim();
        s.TitleAr = vm.TitleAr.Trim();
        s.DescriptionEn = vm.DescriptionEn?.Trim();
        s.DescriptionAr = vm.DescriptionAr?.Trim();
        s.IsActive = vm.IsActive;
        s.OpensAtUtc = vm.OpensAtUtc;
        s.ClosesAtUtc = vm.ClosesAtUtc;
        if (s.Purpose == SurveyPurpose.OfficialSatisfaction) s.SeasonId = vm.SeasonId;
        else s.ActivityId = vm.ActivityId;
        s.PublicToken ??= NewToken();
        s.UpdatedAtUtc = DateTime.UtcNow;
        s.UpdatedByUserId = CurrentUserId;

        await Db.SaveChangesAsync();
        await AuditAsync("Update", nameof(Survey), s.Id.ToString(), old, new { s.TitleEn, s.SeasonId, s.ActivityId, s.IsActive, s.OpensAtUtc, s.ClosesAtUtc });

        TempData["ToastSuccess"] = IsAr() ? "تم تحديث الاستبيان." : "Survey updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var s = await Db.Surveys.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        // Opening a survey with no questions produces a page a participant cannot answer and a
        // response row with nothing in it.
        if (!s.IsActive && !await Db.SurveyQuestions.AnyAsync(x => x.SurveyId == s.Id))
        {
            TempData["ToastWarning"] = IsAr()
                ? "أضف سؤالاً واحداً على الأقل قبل فتح الاستبيان."
                : "Add at least one question before opening the survey.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        var old = new { s.IsActive };
        s.IsActive = !s.IsActive;
        s.PublicToken ??= NewToken();
        s.UpdatedAtUtc = DateTime.UtcNow;
        s.UpdatedByUserId = CurrentUserId;
        await Db.SaveChangesAsync();
        await AuditAsync(s.IsActive ? "SurveyOpened" : "SurveyClosed", nameof(Survey), s.Id.ToString(), old, new { s.IsActive });

        TempData["ToastSuccess"] = s.IsActive
            ? IsAr() ? "تم فتح الاستبيان." : "Survey opened."
            : IsAr() ? "تم إغلاق الاستبيان." : "Survey closed.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- questions

    public async Task<IActionResult> Questions(int id)
    {
        var s = await Db.Surveys
            .Include(x => x.Season)
            .Include(x => x.Activity)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        ViewBag.ResponseCount = await Db.SurveyResponses.CountAsync(x => x.SurveyId == id);
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(int id, QuestionVm vm)
    {
        var s = await Db.Surveys.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        if (string.IsNullOrWhiteSpace(vm.QuestionEn) || string.IsNullOrWhiteSpace(vm.QuestionAr))
        {
            TempData["ToastWarning"] = IsAr()
                ? "نص السؤال مطلوب باللغتين العربية والإنجليزية."
                : "The question text is required in both English and Arabic.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        var next = await Db.SurveyQuestions.Where(x => x.SurveyId == id).MaxAsync(x => (int?)x.SortOrder) ?? 0;
        var q = new SurveyQuestion
        {
            SurveyId = id,
            QuestionEn = vm.QuestionEn.Trim(),
            QuestionAr = vm.QuestionAr.Trim(),
            QuestionType = vm.QuestionType,
            IsRequired = vm.IsRequired,
            SortOrder = next + 1
        };
        Db.SurveyQuestions.Add(q);
        await Db.SaveChangesAsync();
        await AuditAsync("SurveyQuestionAdded", nameof(SurveyQuestion), q.Id.ToString(), null, new { q.SurveyId, q.QuestionEn, q.QuestionType });

        TempData["ToastSuccess"] = IsAr() ? "تمت إضافة السؤال." : "Question added.";
        return RedirectToAction(nameof(Questions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(int id, int questionId, QuestionVm vm)
    {
        var q = await Db.SurveyQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.SurveyId == id);
        if (q is null) return NotFound();

        if (string.IsNullOrWhiteSpace(vm.QuestionEn) || string.IsNullOrWhiteSpace(vm.QuestionAr))
        {
            TempData["ToastWarning"] = IsAr()
                ? "نص السؤال مطلوب باللغتين العربية والإنجليزية."
                : "The question text is required in both English and Arabic.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        var old = new { q.QuestionEn, q.QuestionAr, q.QuestionType, q.IsRequired };
        q.QuestionEn = vm.QuestionEn.Trim();
        q.QuestionAr = vm.QuestionAr.Trim();
        q.IsRequired = vm.IsRequired;

        // Changing the type of a question that already has answers would leave those answers stored in
        // a column the new type never reads — present in the database, absent from every total.
        var hasAnswers = await Db.SurveyAnswers.AnyAsync(x => x.SurveyQuestionId == q.Id);
        if (!hasAnswers) q.QuestionType = vm.QuestionType;
        else if (q.QuestionType != vm.QuestionType)
        {
            TempData["ToastWarning"] = IsAr()
                ? "لا يمكن تغيير نوع سؤال لديه إجابات مسجلة. تم حفظ باقي التعديلات."
                : "The type of a question that already has answers cannot be changed. Other edits were saved.";
        }

        await Db.SaveChangesAsync();
        await AuditAsync("SurveyQuestionUpdated", nameof(SurveyQuestion), q.Id.ToString(), old, new { q.QuestionEn, q.QuestionAr, q.QuestionType, q.IsRequired });
        return RedirectToAction(nameof(Questions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id, int questionId)
    {
        var q = await Db.SurveyQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.SurveyId == id);
        if (q is null) return NotFound();

        // Submitted answers are evidence of what participants were asked. Removing the question would
        // strand them, so the question stays and only its future use is stopped.
        if (await Db.SurveyAnswers.AnyAsync(x => x.SurveyQuestionId == q.Id))
        {
            TempData["ToastWarning"] = IsAr()
                ? "لا يمكن حذف سؤال لديه إجابات مسجلة. أغلق الاستبيان بدلاً من ذلك."
                : "A question that already has answers cannot be deleted. Close the survey instead.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        Db.SurveyOptions.RemoveRange(Db.SurveyOptions.Where(x => x.SurveyQuestionId == q.Id));
        Db.SurveyQuestions.Remove(q);
        await Db.SaveChangesAsync();
        await AuditAsync("SurveyQuestionDeleted", nameof(SurveyQuestion), questionId.ToString(), new { q.QuestionEn }, null);

        await ResequenceAsync(id);
        TempData["ToastWarning"] = IsAr() ? "تم حذف السؤال." : "Question deleted.";
        return RedirectToAction(nameof(Questions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveQuestion(int id, int questionId, string direction)
    {
        var questions = await Db.SurveyQuestions.Where(x => x.SurveyId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var index = questions.FindIndex(x => x.Id == questionId);
        if (index < 0) return NotFound();

        var target = direction == "up" ? index - 1 : index + 1;
        if (target < 0 || target >= questions.Count) return RedirectToAction(nameof(Questions), new { id });

        (questions[index], questions[target]) = (questions[target], questions[index]);
        for (var i = 0; i < questions.Count; i++) questions[i].SortOrder = i + 1;
        await Db.SaveChangesAsync();

        return RedirectToAction(nameof(Questions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOption(int id, int questionId, string? optionEn, string? optionAr)
    {
        var q = await Db.SurveyQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.SurveyId == id);
        if (q is null) return NotFound();

        if (string.IsNullOrWhiteSpace(optionEn) || string.IsNullOrWhiteSpace(optionAr))
        {
            TempData["ToastWarning"] = IsAr()
                ? "نص الخيار مطلوب باللغتين."
                : "The option text is required in both languages.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        var next = await Db.SurveyOptions.Where(x => x.SurveyQuestionId == questionId).MaxAsync(x => (int?)x.SortOrder) ?? 0;
        Db.SurveyOptions.Add(new SurveyOption
        {
            SurveyQuestionId = questionId,
            OptionEn = optionEn.Trim(),
            OptionAr = optionAr.Trim(),
            SortOrder = next + 1
        });
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Questions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOption(int id, int optionId)
    {
        var option = await Db.SurveyOptions
            .Include(x => x.SurveyQuestion)
            .FirstOrDefaultAsync(x => x.Id == optionId && x.SurveyQuestion != null && x.SurveyQuestion.SurveyId == id);
        if (option is null) return NotFound();

        if (await Db.SurveyAnswers.AnyAsync(x => x.SelectedOptionId == optionId))
        {
            TempData["ToastWarning"] = IsAr()
                ? "لا يمكن حذف خيار تم اختياره في إجابات مسجلة."
                : "An option that participants have already selected cannot be deleted.";
            return RedirectToAction(nameof(Questions), new { id });
        }

        Db.SurveyOptions.Remove(option);
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Questions), new { id });
    }

    // ---------------------------------------------------------------- sharing

    /// <summary>The shareable participant link and its QR code, for clubs to distribute.</summary>
    public async Task<IActionResult> Share(int id)
    {
        var s = await Db.Surveys.Include(x => x.Season).FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        if (s.PublicToken is null)
        {
            s.PublicToken = NewToken();
            await Db.SaveChangesAsync();
        }

        ViewBag.ShareUrl = PublicSurveyUrl(s.PublicToken);
        return View(s);
    }

    /// <summary>PNG QR code for the shareable link.</summary>
    public async Task<IActionResult> QrCode(int id)
    {
        var s = await Db.Surveys.FirstOrDefaultAsync(x => x.Id == id);
        if (s?.PublicToken is null) return NotFound();
        return File(QrCodeHelper.GeneratePng(PublicSurveyUrl(s.PublicToken)), "image/png");
    }

    private string PublicSurveyUrl(string token)
        => $"{Request.Scheme}://{Request.Host}{Url.Content($"~/surveys/take/{token}")}";

    // ---------------------------------------------------------------- results

    public async Task<IActionResult> Results(int id, int? organizationId = null)
    {
        var survey = await Db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Season)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();

        var responses = Db.SurveyResponses.Where(x => x.SurveyId == id);
        if (organizationId.HasValue) responses = responses.Where(x => x.OrganizationId == organizationId.Value);

        var loaded = await responses
            .Include(x => x.Answers)
            .Include(x => x.Organization)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync();

        ViewBag.Breakdown = BuildBreakdown(survey, loaded);
        ViewBag.TotalResponses = loaded.Count;
        ViewBag.AnonymousResponses = loaded.Count(x => x.UserId is null);

        var stars = loaded.SelectMany(x => x.Answers)
            .Where(a => a.StarsValue != null && survey.Questions.Any(q => q.Id == a.SurveyQuestionId && q.QuestionType == SurveyQuestionType.Stars))
            .Select(a => (int)a.StarsValue!)
            .ToList();

        ViewBag.AvgStars = stars.Count == 0 ? 0d : stars.Average();
        // One formula, shared with the KPI and the dashboard. See SatisfactionCalculator.
        ViewBag.SatisfactionPercent = SatisfactionCalculator.PercentFromStars(stars);
        ViewBag.MinimumResponses = SatisfactionCalculator.MinimumResponses;

        // Only approved Ghars clubs appear in the filter, and only those that actually responded.
        var respondingClubIds = await Db.SurveyResponses
            .Where(x => x.SurveyId == id && x.OrganizationId != null)
            .Select(x => x.OrganizationId!.Value)
            .Distinct()
            .ToListAsync();
        ViewBag.Clubs = await Db.Organizations
            .ApprovedClubs()
            .Where(x => respondingClubIds.Contains(x.Id))
            .ToListAsync();
        ViewBag.OrganizationId = organizationId;

        // Per-club totals, so DSC can see the spread rather than only the headline.
        ViewBag.ByClub = loaded
            .Where(x => x.Organization != null)
            .GroupBy(x => x.Organization!)
            .Select(g => new ClubResultRow(
                g.Key.NameEn,
                g.Key.NameAr,
                g.Count(),
                SatisfactionCalculator.PercentFromStars(
                    g.SelectMany(r => r.Answers)
                     .Where(a => a.StarsValue != null && survey.Questions.Any(q => q.Id == a.SurveyQuestionId && q.QuestionType == SurveyQuestionType.Stars))
                     .Select(a => (int)a.StarsValue!)
                     .ToList())))
            .OrderByDescending(x => x.Responses)
            .ToList();

        return View(survey);
    }

    /// <summary>
    /// Per-question aggregation. Reports only what the stored answers support: rating averages and
    /// distributions, yes/no splits, option counts, and the free-text comments themselves.
    /// </summary>
    internal static List<QuestionBreakdown> BuildBreakdown(Survey survey, List<SurveyResponse> responses)
    {
        var answers = responses.SelectMany(x => x.Answers).ToList();
        var result = new List<QuestionBreakdown>();

        foreach (var q in survey.Questions.OrderBy(x => x.SortOrder))
        {
            var mine = answers.Where(a => a.SurveyQuestionId == q.Id).ToList();
            var row = new QuestionBreakdown(q, mine.Count);

            switch (q.QuestionType)
            {
                case SurveyQuestionType.Stars:
                    var stars = mine.Where(a => a.StarsValue != null).Select(a => (int)a.StarsValue!).ToList();
                    row.Average = stars.Count == 0 ? null : Math.Round(stars.Average(), 2);
                    row.Percent = SatisfactionCalculator.PercentFromStars(stars);
                    for (var i = 1; i <= 5; i++)
                        row.Distribution.Add((i.ToString(), i.ToString(), stars.Count(v => v == i)));
                    break;

                case SurveyQuestionType.YesNo:
                    var yes = mine.Count(a => a.BoolValue == true);
                    var no = mine.Count(a => a.BoolValue == false);
                    row.Distribution.Add(("Yes", "نعم", yes));
                    row.Distribution.Add(("No", "لا", no));
                    row.Percent = yes + no == 0 ? null : Math.Round((decimal)yes / (yes + no) * 100m, 1);
                    break;

                case SurveyQuestionType.Mcq:
                    foreach (var option in q.Options.OrderBy(o => o.SortOrder))
                        row.Distribution.Add((option.OptionEn, option.OptionAr, mine.Count(a => a.SelectedOptionId == option.Id)));
                    break;

                case SurveyQuestionType.Text:
                    row.Comments.AddRange(mine
                        .Select(a => a.TextValue)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t!.Trim())
                        .Take(200));
                    break;
            }

            result.Add(row);
        }

        return result;
    }

    // ---------------------------------------------------------------- helpers

    private async Task LoadLookupsAsync()
    {
        ViewBag.Activities = await Db.Activities
            .Where(x => x.Status == ActivityStatus.Published)
            .OrderByDescending(x => x.StartDateTime)
            .Take(200)
            .ToListAsync();
        ViewBag.Seasons = await Db.Seasons.OrderByDescending(x => x.StartDate).ToListAsync();
    }

    private async Task ValidateAsync(SurveyVm vm, int? existingId)
    {
        var isAr = IsAr();

        if (vm.Purpose == SurveyPurpose.OfficialSatisfaction)
        {
            if (vm.SeasonId is null)
            {
                ModelState.AddModelError(nameof(vm.SeasonId), isAr
                    ? "يجب ربط الاستبيان الرسمي بموسم رياضي."
                    : "The official survey must be linked to a sports season.");
            }
            else
            {
                // One official satisfaction survey per season. The database enforces this too; the
                // check here exists to say so in words rather than as a constraint violation.
                var clash = await Db.Surveys.AnyAsync(x =>
                    x.Purpose == SurveyPurpose.OfficialSatisfaction &&
                    x.SeasonId == vm.SeasonId &&
                    (existingId == null || x.Id != existingId));
                if (clash)
                {
                    ModelState.AddModelError(nameof(vm.SeasonId), isAr
                        ? "يوجد بالفعل استبيان رضا رسمي لهذا الموسم."
                        : "An official satisfaction survey already exists for that season.");
                }
            }
        }
        else if (vm.ActivityId is null || !await Db.Activities.AnyAsync(x => x.Id == vm.ActivityId))
        {
            ModelState.AddModelError(nameof(vm.ActivityId), isAr
                ? "يجب ربط استبيان النشاط بنشاط منشور."
                : "An activity survey must be linked to a published activity.");
        }

        if (vm.OpensAtUtc.HasValue && vm.ClosesAtUtc.HasValue && vm.ClosesAtUtc <= vm.OpensAtUtc)
        {
            ModelState.AddModelError(nameof(vm.ClosesAtUtc), isAr
                ? "يجب أن يكون تاريخ الإغلاق بعد تاريخ الفتح."
                : "The closing date must be after the opening date.");
        }
    }

    private async Task ResequenceAsync(int surveyId)
    {
        var questions = await Db.SurveyQuestions.Where(x => x.SurveyId == surveyId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        for (var i = 0; i < questions.Count; i++) questions[i].SortOrder = i + 1;
        await Db.SaveChangesAsync();
    }

    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // ---------------------------------------------------------------- view models

    public sealed class QuestionBreakdown
    {
        public QuestionBreakdown(SurveyQuestion question, int answerCount)
        {
            Question = question;
            AnswerCount = answerCount;
        }

        public SurveyQuestion Question { get; }
        public int AnswerCount { get; }
        public double? Average { get; set; }
        public decimal? Percent { get; set; }
        public List<(string LabelEn, string LabelAr, int Count)> Distribution { get; } = new();
        public List<string> Comments { get; } = new();
    }

    public sealed record ClubResultRow(string NameEn, string NameAr, int Responses, decimal? Percent);

    public class SurveyVm
    {
        public int Id { get; set; }

        public SurveyPurpose Purpose { get; set; } = SurveyPurpose.General;

        public int? ActivityId { get; set; }
        public int? SeasonId { get; set; }

        [Required, MaxLength(200)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(200)]
        public string TitleAr { get; set; } = "";

        [MaxLength(2000)] public string? DescriptionEn { get; set; }
        [MaxLength(2000)] public string? DescriptionAr { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? OpensAtUtc { get; set; }
        public DateTime? ClosesAtUtc { get; set; }
    }

    public class QuestionVm
    {
        public string? QuestionEn { get; set; }
        public string? QuestionAr { get; set; }
        public SurveyQuestionType QuestionType { get; set; } = SurveyQuestionType.Stars;
        public bool IsRequired { get; set; } = true;
    }
}
