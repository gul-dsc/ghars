using System.Security.Claims;
using System.Text.Encodings.Web;
using GharsPlatform.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GharsPlatform.Tests.Infrastructure;

/// <summary>
/// Signs a request in as the user named in the <see cref="Header"/> header, with the roles the
/// database currently holds for that user — so role changes made by a test take effect on the next
/// request, exactly as a fresh sign-in would. No header: anonymous.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var values) || string.IsNullOrWhiteSpace(values.ToString()))
            return AuthenticateResult.NoResult();

        var users = Context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(values.ToString());
        if (user is null) return AuthenticateResult.Fail("Unknown test user.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Id)
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
