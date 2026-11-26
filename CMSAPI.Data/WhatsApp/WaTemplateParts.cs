using System.Text.Json;
using System.Text.Json.Nodes;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// A template's parts, read from Meta's components JSON, and the two things built from them: the
/// request that creates a template, and the message that sends it with each person's values.
///
/// Values when sending: header variables as "h.1" (or "h.name"), body variables as "1" (or "name"),
/// a URL button's variable as "url", a copy-code button's code as "code".
/// </summary>
public class WaTemplateParts
{
    public string HeaderFormat { get; set; } = "NONE";
    public string? HeaderText { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }
    public List<WaButton> Buttons { get; set; } = new();
    public bool Named { get; set; }

    public List<string> HeaderVariables => WaRules.VariablesIn(HeaderText);
    public List<string> BodyVariables => WaRules.VariablesIn(Body);
    public int UrlButtonIndex => Buttons.FindIndex(b => b.Type == "URL" && WaRules.VariablesIn(b.Url).Count > 0);
    public int CopyCodeIndex => Buttons.FindIndex(b => b.Type == "COPY_CODE");
    public bool HasMediaHeader => HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT";

    /// <summary>Every value a send needs, in the keys described above.</summary>
    public List<string> RequiredValues()
    {
        var keys = HeaderVariables.Select(v => "h." + v).Concat(BodyVariables).ToList();
        if (UrlButtonIndex >= 0) keys.Add("url");
        if (CopyCodeIndex >= 0) keys.Add("code");
        return keys;
    }

    public static WaTemplateParts Parse(string componentsJson, string? parameterFormat)
    {
        var parts = new WaTemplateParts { Named = string.Equals(parameterFormat, "NAMED", StringComparison.OrdinalIgnoreCase) };
        JsonArray? components = null;
        try { components = JsonNode.Parse(componentsJson) as JsonArray; } catch (JsonException) { }
        foreach (var c in components ?? new JsonArray())
        {
            switch (c?["type"].Str()?.ToUpperInvariant())
            {
                case "HEADER":
                    parts.HeaderFormat = c["format"].Str()?.ToUpperInvariant() ?? "TEXT";
                    parts.HeaderText = c["text"].Str();
                    break;
                case "BODY":
                    parts.Body = c["text"].Str() ?? string.Empty;
                    break;
                case "FOOTER":
                    parts.Footer = c["text"].Str();
                    break;
                case "BUTTONS":
                    foreach (var b in c["buttons"]?.AsArray() ?? new JsonArray())
                    {
                        if (b == null) continue;
                        parts.Buttons.Add(new WaButton
                        {
                            Type = b["type"].Str()?.ToUpperInvariant() ?? "QUICK_REPLY",
                            Text = b["text"].Str(),
                            Url = b["url"].Str(),
                            Phone = b["phone_number"].Str(),
                            Example = b["example"] is JsonArray ex ? ex[0].Str() : b["example"].Str(),
                        });
                    }
                    break;
            }
        }
        // Named templates are marked by parameter_format; older lists may not say, so look at the text.
        if (!parts.Named && parts.BodyVariables.Concat(parts.HeaderVariables).Any(v => !int.TryParse(v, out _))) parts.Named = true;
        return parts;
    }

    /// <summary>The body as a person reads it: header line, body, footer, with values filled in.</summary>
    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var header = HeaderText != null
            ? WaRules.Fill(HeaderText, values.Where(v => v.Key.StartsWith("h.")).ToDictionary(v => v.Key[2..], v => v.Value))
            : null;
        var body = WaRules.Fill(Body, values.Where(v => !v.Key.StartsWith("h.")).ToDictionary(v => v.Key, v => v.Value));
        return string.Join("\n\n", new[] { header != null ? $"*{header}*" : null, body, Footer }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// The "template" part of a message. headerMedia: Meta media id (or https link) of the picture, video or
    /// document for a media header.
    /// </summary>
    public JsonObject SendPayload(string name, string language, IReadOnlyDictionary<string, string> values, string? headerMediaId, string? headerMediaLink, string? documentName)
    {
        var components = new JsonArray();
        JsonObject Text(string variable, string value)
        {
            var p = new JsonObject { ["type"] = "text", ["text"] = value };
            if (Named) p["parameter_name"] = variable;
            return p;
        }

        if (HasMediaHeader)
        {
            var kind = HeaderFormat.ToLowerInvariant();
            var media = new JsonObject();
            if (headerMediaId != null) media["id"] = headerMediaId; else if (headerMediaLink != null) media["link"] = headerMediaLink;
            if (kind == "document" && documentName != null) media["filename"] = documentName;
            components.Add(new JsonObject
            {
                ["type"] = "header",
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = kind, [kind] = media }),
            });
        }
        else if (HeaderVariables.Count > 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "header",
                ["parameters"] = new JsonArray(HeaderVariables.Select(v => (JsonNode)Text(v, Value(values, "h." + v))).ToArray()),
            });
        }
        if (BodyVariables.Count > 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(BodyVariables.Select(v => (JsonNode)Text(v, Value(values, v))).ToArray()),
            });
        }
        if (UrlButtonIndex >= 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "button", ["sub_type"] = "url", ["index"] = UrlButtonIndex.ToString(),
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Value(values, "url") }),
            });
        }
        if (CopyCodeIndex >= 0)
        {
            components.Add(new JsonObject
            {
                ["type"] = "button", ["sub_type"] = "copy_code", ["index"] = CopyCodeIndex.ToString(),
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "coupon_code", ["coupon_code"] = Value(values, "code") }),
            });
        }

        var template = new JsonObject { ["name"] = name, ["language"] = new JsonObject { ["code"] = language } };
        if (components.Count > 0) template["components"] = components;
        return template;
    }

    // Meta refuses an empty parameter: a missing value becomes a dash rather than failing the whole send.
    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : "-";

    /// <summary>What is wrong with a new template, in words; null when Meta can be asked.</summary>
    public static string? Check(WaSaveTemplateRequest r)
    {
        if (!WaRules.ValidTemplateName(r.Name)) return "The name may only have lower case letters, digits and _ (like diwali_offer_2026).";
        if (!WaRules.Categories.Contains(r.Category)) return "Choose Marketing, Utility or Authentication.";
        if (string.IsNullOrWhiteSpace(r.Language) || r.Language.Length > 15) return "Choose the template's language.";
        if (string.IsNullOrWhiteSpace(r.Body)) return "The message text is required.";
        if (r.Body.Length > WaRules.MaxBody) return $"The message text can be at most {WaRules.MaxBody} characters.";
        var named = WaRules.VariablesIn(r.Body).Concat(WaRules.VariablesIn(r.HeaderText)).Any(v => !int.TryParse(v, out _));
        if (WaRules.CheckVariables(r.Body, named, "message text") is { } bodyProblem) return bodyProblem;
        foreach (var v in WaRules.VariablesIn(r.Body))
            if (!r.BodyExamples.TryGetValue(v, out var ex) || string.IsNullOrWhiteSpace(ex))
                return $"Give an example for {{{{{v}}}}}: Meta checks templates with real-looking examples.";
        if (!new[] { "NONE", "TEXT", "IMAGE", "VIDEO", "DOCUMENT" }.Contains(r.HeaderFormat)) return "Choose a header: none, text, picture, video or document.";
        if (r.HeaderFormat == "TEXT")
        {
            if (string.IsNullOrWhiteSpace(r.HeaderText)) return "Write the header, or choose no header.";
            if (r.HeaderText.Length > WaRules.MaxHeaderText) return $"The header can be at most {WaRules.MaxHeaderText} characters.";
            var hv = WaRules.VariablesIn(r.HeaderText);
            if (hv.Count > 1) return "The header can have one variable at most.";
            if (hv.Count == 1 && string.IsNullOrWhiteSpace(r.HeaderExample)) return "Give an example for the header's variable.";
        }
        if (r.Footer?.Length > WaRules.MaxFooter) return $"The footer can be at most {WaRules.MaxFooter} characters.";
        if (WaRules.VariablesIn(r.Footer).Count > 0) return "The footer cannot have variables.";
        if (r.Buttons.Count > WaRules.MaxButtons) return $"A template can have {WaRules.MaxButtons} buttons at most.";
        if (r.Buttons.Count(b => b.Type == "URL") > 2) return "A template can have two link buttons at most.";
        if (r.Buttons.Count(b => b.Type == "PHONE_NUMBER") > 1) return "A template can have one call button at most.";
        if (r.Buttons.Count(b => b.Type == "COPY_CODE") > 1) return "A template can have one copy-code button at most.";
        // Quick replies must sit together (all before or all after the others).
        var kinds = r.Buttons.Select(b => b.Type == "QUICK_REPLY").ToList();
        var switches = kinds.Zip(kinds.Skip(1), (a, b) => a != b).Count(x => x);
        if (switches > 1) return "Put the quick-reply buttons together, before or after the other buttons.";
        foreach (var b in r.Buttons)
        {
            if (b.Type != "COPY_CODE" && (string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > WaRules.MaxButtonText))
                return $"Each button needs a label of up to {WaRules.MaxButtonText} characters.";
            if (b.Type == "URL")
            {
                if (string.IsNullOrWhiteSpace(b.Url) || !b.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "A link button needs a https:// address.";
                var uv = WaRules.VariablesIn(b.Url);
                if (uv.Count > 1 || (uv.Count == 1 && !b.Url.TrimEnd().EndsWith("}}"))) return "A link button can have one variable, at the end of the address.";
                if (uv.Count == 1 && string.IsNullOrWhiteSpace(b.UrlExample)) return "Give an example for the link's variable.";
            }
            if (b.Type == "PHONE_NUMBER" && WaRules.NormalisePhone(b.Phone) == null) return "The call button needs a phone number with its country code.";
            if (b.Type == "COPY_CODE" && (string.IsNullOrWhiteSpace(b.Example) || b.Example.Length > 15)) return "The copy-code button needs an example code of up to 15 characters.";
            if (b.Type is not ("QUICK_REPLY" or "URL" or "PHONE_NUMBER" or "COPY_CODE")) return "Unknown button type.";
        }
        if (r.Category == "AUTHENTICATION") return "Authentication (one-time password) templates are made in WhatsApp Manager: they follow Meta's fixed wording.";
        return null;
    }

    /// <summary>The request Meta needs to create the template. headerHandle: from the sample upload.</summary>
    public static JsonObject CreatePayload(WaSaveTemplateRequest r, string? headerHandle)
    {
        var named = WaRules.VariablesIn(r.Body).Concat(WaRules.VariablesIn(r.HeaderText)).Any(v => !int.TryParse(v, out _));
        var components = new JsonArray();

        if (r.HeaderFormat == "TEXT")
        {
            var header = new JsonObject { ["type"] = "HEADER", ["format"] = "TEXT", ["text"] = r.HeaderText!.Trim() };
            var hv = WaRules.VariablesIn(r.HeaderText);
            if (hv.Count == 1)
                header["example"] = named
                    ? new JsonObject { ["header_text_named_params"] = new JsonArray(new JsonObject { ["param_name"] = hv[0], ["example"] = r.HeaderExample!.Trim() }) }
                    : new JsonObject { ["header_text"] = new JsonArray(r.HeaderExample!.Trim()) };
            components.Add(header);
        }
        else if (r.HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT")
        {
            components.Add(new JsonObject
            {
                ["type"] = "HEADER", ["format"] = r.HeaderFormat,
                ["example"] = new JsonObject { ["header_handle"] = new JsonArray(headerHandle) },
            });
        }

        var body = new JsonObject { ["type"] = "BODY", ["text"] = r.Body.Trim() };
        var bv = WaRules.VariablesIn(r.Body);
        if (bv.Count > 0)
        {
            body["example"] = named
                ? new JsonObject { ["body_text_named_params"] = new JsonArray(bv.Select(v => (JsonNode)new JsonObject { ["param_name"] = v, ["example"] = r.BodyExamples[v].Trim() }).ToArray()) }
                : new JsonObject { ["body_text"] = new JsonArray(new JsonArray(bv.OrderBy(int.Parse).Select(v => (JsonNode)JsonValue.Create(r.BodyExamples[v].Trim())!).ToArray())) };
        }
        components.Add(body);

        if (!string.IsNullOrWhiteSpace(r.Footer)) components.Add(new JsonObject { ["type"] = "FOOTER", ["text"] = r.Footer.Trim() });

        if (r.Buttons.Count > 0)
        {
            var buttons = new JsonArray();
            foreach (var b in r.Buttons)
            {
                var button = new JsonObject { ["type"] = b.Type };
                switch (b.Type)
                {
                    case "QUICK_REPLY": button["text"] = b.Text!.Trim(); break;
                    case "URL":
                        button["text"] = b.Text!.Trim();
                        button["url"] = b.Url!.Trim();
                        if (WaRules.VariablesIn(b.Url).Count == 1) button["example"] = new JsonArray(b.UrlExample!.Trim());
                        break;
                    case "PHONE_NUMBER":
                        button["text"] = b.Text!.Trim();
                        button["phone_number"] = "+" + WaRules.NormalisePhone(b.Phone);
                        break;
                    case "COPY_CODE": button["example"] = b.Example!.Trim(); break;
                }
                buttons.Add(button);
            }
            components.Add(new JsonObject { ["type"] = "BUTTONS", ["buttons"] = buttons });
        }

        var payload = new JsonObject
        {
            ["name"] = r.Name,
            ["language"] = r.Language,
            ["category"] = r.Category,
            ["components"] = components,
        };
        if (named) payload["parameter_format"] = "NAMED";
        return payload;
    }
}
