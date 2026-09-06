using System.ComponentModel.DataAnnotations;
using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Admin;

[Area("Admin")]
[Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.DscAdmin}")]
public class SurveysController : Controllers.BaseController
{
    public SurveysController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var list = await Db.Surveys
            .Include(x => x.Activity)
            .OrderByDescending(x => x.Id)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Activities = await Db.Activities.Where(x => x.Status == ActivityStatus.Published)
            .OrderByDescending(x => x.StartDateTime).ToListAsync();
        return View(new SurveyVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SurveyVm vm)
    {
        ViewBag.Activities = await Db.Activities.Where(x => x.Status == ActivityStatus.Published)
            .OrderByDescending(x => x.StartDateTime).ToListAsync();

        if (!ModelState.IsValid) return View(vm);

        var s = new Survey
        {
            ActivityId = vm.ActivityId,
            TitleEn = vm.TitleEn.Trim(),
            TitleAr = vm.TitleAr.Trim(),
            IsActive = vm.IsActive,
            OpensAtUtc = vm.OpensAtUtc,
            ClosesAtUtc = vm.ClosesAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        Db.Surveys.Add(s);
        await Db.SaveChangesAsync();

        // Default questions (government standard: satisfaction + speaker + organization + comments)
        Db.SurveyQuestions.AddRange(
            new SurveyQuestion { SurveyId = s.Id, QuestionEn = "Overall satisfaction (1-5)", QuestionAr = "الرضا العام (١-٥)", QuestionType = SurveyQuestionType.Stars, SortOrder = 1 },
            new SurveyQuestion { SurveyId = s.Id, QuestionEn = "Content quality (1-5)", QuestionAr = "جودة المحتوى (١-٥)", QuestionType = SurveyQuestionType.Stars, SortOrder = 2 },
            new SurveyQuestion { SurveyId = s.Id, QuestionEn = "Would you recommend it to others?", QuestionAr = "هل توصي بها للآخرين؟", QuestionType = SurveyQuestionType.YesNo, SortOrder = 3 },
            new SurveyQuestion { SurveyId = s.Id, QuestionEn = "Comments / Suggestions", QuestionAr = "ملاحظات / اقتراحات", QuestionType = SurveyQuestionType.Text, IsRequired = false, SortOrder = 4 }
        );

        await Db.SaveChangesAsync();

        TempData["ToastSuccess"] = "Survey created with default questions.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Results(int id)
    {
        var survey = await Db.Surveys
            .Include(x => x.Activity)
            .Include(x => x.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);

        if (survey is null) return NotFound();

        var responses = await Db.SurveyResponses
            .Where(x => x.SurveyId == id)
            .Include(x => x.Answers)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync();

        ViewBag.Responses = responses;

        // Summary stats
        var starAnswers = responses.SelectMany(r => r.Answers).Where(a => a.StarsValue != null).Select(a => (int)a.StarsValue!).ToList();
        ViewBag.AvgStars = starAnswers.Count == 0 ? 0 : starAnswers.Average();
        ViewBag.TotalResponses = responses.Count;

        return View(survey);
    }

    public class SurveyVm
    {
        [Required]
        public int ActivityId { get; set; }

        [Required, MaxLength(200)]
        public string TitleEn { get; set; } = "";

        [Required, MaxLength(200)]
        public string TitleAr { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime? OpensAtUtc { get; set; }
        public DateTime? ClosesAtUtc { get; set; }
    }
}
