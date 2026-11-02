using System.Security.Claims;
using CMSAPI.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Shared by the two Health Wiki controllers: who is acting, and how an answer from 1HMSAPI is handed to the CMS web
// ({ success, data } like the rest of this API, or { success: false, message }).
public abstract class HealthWikiControllerBase : ControllerBase
{
    // The signed-in CMS user's name, which 1HMSAPI records in the article's history.
    protected string Actor => User.FindFirst("name")?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "CMS";

    protected IActionResult Reply(HealthWikiProxyResponse r)
    {
        if (!r.Success)
            return StatusCode(r.StatusCode, new { success = false, message = r.Message, diagnostic = r.Diagnostic });

        // link and linkDelivered appear only after an invite or a send-link.
        return r.Link != null || r.LinkDelivered != null
            ? StatusCode(r.StatusCode, new { success = true, data = r.Data, link = r.Link, linkDelivered = r.LinkDelivered })
            : StatusCode(r.StatusCode, new { success = true, data = r.Data });
    }
}
