using GharsPlatform.Data;
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
        if (string.IsNullOrWhiteSpace(token))
            return NotFound();

        var cert = await _db.Certificates
            .Include(x => x.Activity)
            .FirstOrDefaultAsync(x => x.VerifyToken == token);

        if (cert is null) return NotFound();

        return View(cert);
    }
}
