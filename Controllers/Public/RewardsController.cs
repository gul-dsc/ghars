using Microsoft.AspNetCore.Mvc;

namespace GharsPlatform.Controllers.Public;

// Rewards are intentionally disabled in the user-facing UI. The tables remain for historical data only.
public class RewardsController : Controller
{
    [HttpGet("/rewards")]
    public IActionResult Index() => NotFound();

    [HttpPost("/rewards/redeem/{id:int}")]
    [ValidateAntiForgeryToken]
    public IActionResult Redeem(int id) => NotFound();
}
