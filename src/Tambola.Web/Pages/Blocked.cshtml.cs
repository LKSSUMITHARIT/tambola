using Microsoft.AspNetCore.Mvc.RazorPages;
using Tambola.Web.Auth;

namespace Tambola.Web.Pages;

/// <summary>Shown to authenticated users whose domain is not in AllowedDomains.</summary>
public class BlockedModel : PageModel
{
    public string Domain => User.FindFirst(Claims.Blocked)?.Value ?? "";
}
