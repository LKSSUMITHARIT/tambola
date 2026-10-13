using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tambola.Web.Pages;

[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string Code => Request.Query["code"].ToString();
}
