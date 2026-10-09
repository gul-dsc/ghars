using GharsPlatform.Data;
using GharsPlatform.Models.Core;
using GharsPlatform.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GharsPlatform.Controllers.Public;

public class VerifyController : Controller
{
    private readonly AppDbContext _db;

    public VerifyController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/verify/certificate/{token}")]
    public async Task<IActionResult> Certificate(string token)
    {
        // An unknown or empty token still answers 404, but with a readable bilingual page.
        if (string.IsNullOrWhiteSpace(token))
            return NotVerified();

        // Projected straight to the public view model: the revocation reason, the participant and
        // the stored file path are never loaded for this page.
        var vm = await _db.Certificates
            .AsNoTracking()
            .Where(x => x.VerifyToken == token)
            .Select(x => new CertificateVerificationVm
            {
                CertificateNo = x.CertificateNo,
                IsValid = x.Status == CertificateStatus.Issued,
                IssuedAtUtc = x.IssuedAtUtc,
                ActivityTitleEn = x.Activity != null ? x.Activity.TitleEn : null,
                ActivityTitleAr = x.Activity != null ? x.Activity.TitleAr : null
            })
            .FirstOrDefaultAsync();

        if (vm is null) return NotVerified();

        Response.Headers.CacheControl = "no-store";
        return View(vm);
    }

    private IActionResult NotVerified()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotVerified");
    }
}
