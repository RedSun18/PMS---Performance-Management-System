using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aic.Pm.Web.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/PmForm/Index");
}
