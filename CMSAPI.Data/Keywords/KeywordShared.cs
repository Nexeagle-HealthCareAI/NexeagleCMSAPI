using System.Text.Json;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Keywords;

/// <summary>Keywords settings as the collector and screens use them (KeywordSettings row, script 38).</summary>
public class KeywordSettingsView
{
    public Dictionary<string, List<string>> Seeds { get; set; } = new();
    public Dictionary<string, List<string>> CompetitorSites { get; set; } = new();
    public List<string> ExtraCities { get; set; } = new();
    public KeywordWeights Weights { get; set; } = new();
    public int MinVisitors { get; set; } = 5;
    public int PlannerCursor { get; set; }
}

public static class KeywordShared
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    /// <summary>Seed lists to start from, until marketing edits them in Setup.</summary>
    public static readonly Dictionary<string, List<string>> DefaultSeeds = new()
    {
        [KeywordSites.DoctorDekho] = new()
        {
            "dengue", "typhoid", "sugar", "bp", "thyroid", "kidney stone", "piles", "joint pain", "skin allergy", "pregnancy care",
            "child specialist", "gynecologist", "orthopedic doctor", "skin doctor", "eye doctor", "ent doctor", "dentist", "cardiologist",
        },
        [KeywordSites.NexEagle] = new()
        {
            "hospital management software", "opd billing software", "pmjay claim software", "nabh software", "abdm integration",
            "radiology reporting software", "lab management software", "pharmacy software", "clinic management software", "hospital software india",
        },
    };

    public static async Task<KeywordSettingsView> SettingsAsync(CmsDbContext db, CancellationToken ct)
    {
        var row = await db.KeywordSettings.FirstOrDefaultAsync(ct);
        if (row == null)
        {
            row = new KeywordSettings { SettingsId = 1, UpdatedAt = DateTime.UtcNow };
            db.KeywordSettings.Add(row);
            await db.SaveChangesAsync(ct);
        }
        return View(row);
    }

    public static KeywordSettingsView View(KeywordSettings row) => new()
    {
        Seeds = Read<Dictionary<string, List<string>>>(row.SeedsJson) ?? DefaultSeeds.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
        CompetitorSites = Read<Dictionary<string, List<string>>>(row.CompetitorSitesJson) ?? new(),
        ExtraCities = Read<List<string>>(row.CitiesJson) ?? new(),
        Weights = Read<KeywordWeights>(row.WeightsJson) ?? new(),
        MinVisitors = Math.Max(5, row.MinVisitors),
        PlannerCursor = row.PlannerCursor,
    };
}
