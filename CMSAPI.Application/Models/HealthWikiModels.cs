using System.Text.Json.Nodes;

namespace CMSAPI.Application.Models;

// How to read what 1HMSAPI's Health Wiki internal routes answer with, so the CMS web gets the object it expects in `data`.
public enum HealthWikiShape
{
    // The answer is the data (a list, an object, a count, an image address).
    Whole,
    // A save or a decision: 1HMSAPI answers { success, message, article } and the CMS wants the article.
    Article,
    // An invite, a verification, a link: { success, contributor, link, linkDelivered } and the CMS wants the contributor.
    Contributor,
}

// What the CMS API answers the CMS web with, after talking to 1HMSAPI. StatusCode is what to answer, which is not always what
// 1HMSAPI said: a refused key there is a 502 here, never a 401 (the CMS web reads 401 as "your session ended").
public class HealthWikiProxyResponse
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? Message { get; set; }
    public JsonNode? Data { get; set; }
    // Only on an invite or a send-link: the address to copy, and whether WhatsApp accepted the message.
    public string? Link { get; set; }
    public bool? LinkDelivered { get; set; }
    // For the platform team only (a storage error code when an image could not be saved).
    public string? Diagnostic { get; set; }

    public static HealthWikiProxyResponse Fail(int status, string message, string? diagnostic = null) =>
        new() { Success = false, StatusCode = status, Message = message, Diagnostic = diagnostic };
}
