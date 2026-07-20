using Ganss.Xss;

namespace Phonix.Api.Services;

// Turns the HTML body of an arbitrary inbound email into something safe to put on screen.
//
// This is the single most dangerous surface the admin inbox adds: the body is attacker-controlled text that
// an admin — the highest-privileged session in the shop — is going to open. A script that runs here runs with
// the panel's cookies. So there are two independent layers, and neither is trusted to be sufficient alone:
//
//   1. Server-side allowlist (here). Ganss.Xss keeps only known-good tags/attributes, which drops <script>,
//      every on* handler, javascript: URLs, <iframe>, <object>, <form> and friends.
//   2. Client-side isolation. The panel renders the result in <iframe sandbox> with NO allow-scripts and NO
//      allow-same-origin, so even a sanitizer bypass lands in an origin with no script execution and no
//      access to the panel's DOM or cookies.
//
// Remote content is stripped rather than allowed: a remote <img> is a read receipt and an IP leak to whoever
// sent the mail. The caller is told it happened so the UI can say "images were blocked" the way Gmail does.
public static class MailHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = Build();

    private static HtmlSanitizer Build()
    {
        var s = new HtmlSanitizer();

        // Start from nothing and add back only what an email actually needs to read correctly. The library's
        // defaults are already conservative, but an email body has no business carrying most of them.
        s.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "a", "b", "blockquote", "br", "caption", "code", "col", "colgroup", "dd", "div", "dl", "dt",
            "em", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "li", "ol", "p", "pre", "q", "s",
            "small", "span", "strike", "strong", "sub", "sup", "table", "tbody", "td", "tfoot", "th",
            "thead", "tr", "u", "ul",
        })
            s.AllowedTags.Add(tag);

        s.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "title", "colspan", "rowspan", "align", "dir", "style" })
            s.AllowedAttributes.Add(attr);

        // Only these schemes may appear in an href. Everything else — javascript:, data:, vbscript:, file: —
        // is dropped with the attribute.
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");

        // Inline CSS survives (emails lean on it heavily for layout) but only these properties, so a body
        // cannot position itself over the panel chrome or pull in a remote resource through CSS.
        s.AllowedCssProperties.Clear();
        foreach (var prop in new[]
        {
            "color", "background-color", "font-family", "font-size", "font-style", "font-weight",
            "text-align", "text-decoration", "line-height", "margin", "margin-top", "margin-bottom",
            "margin-left", "margin-right", "padding", "padding-top", "padding-bottom", "padding-left",
            "padding-right", "border", "border-top", "border-bottom", "border-left", "border-right",
            "border-color", "border-radius", "border-style", "border-width", "width", "max-width",
            "height", "vertical-align", "direction", "white-space",
        })
            s.AllowedCssProperties.Add(prop);

        s.AllowedAtRules.Clear();

        // NOTE there is deliberately no img/src handling here: "img" is absent from AllowedTags and "src",
        // "srcset" and "background" are absent from AllowedAttributes, so an inbound image cannot survive at
        // all. CSS cannot smuggle one back in either — background-image and its shorthand are not in the
        // allowed property list, which is the only place a url(...) could have been honored.

        // Links open in a new tab and must not hand the opener over to the target page.
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                a.SetAttribute("target", "_blank");
                a.SetAttribute("rel", "noopener noreferrer nofollow");
            }
        };

        return s;
    }

    // Returns the sanitized HTML plus whether anything remote was removed on the way.
    public static (string Html, bool HadRemoteContent) Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return ("", false);

        // Detected BEFORE sanitizing, because sanitizing is what removes the evidence. This is a heuristic
        // used only to decide whether to show an informational note — never a security decision.
        var hadRemote = ContainsRemoteReference(html);

        string clean;
        try
        {
            clean = Sanitizer.Sanitize(html);
        }
        catch
        {
            // A body that cannot be parsed is a body that must not be rendered. Falling back to empty makes
            // the UI show the plain-text alternative instead, which is always safe.
            return ("", hadRemote);
        }

        return (clean, hadRemote);
    }

    private static bool ContainsRemoteReference(string html) =>
        html.Contains("<img", StringComparison.OrdinalIgnoreCase)
        || html.Contains("background=", StringComparison.OrdinalIgnoreCase)
        || html.Contains("url(", StringComparison.OrdinalIgnoreCase);
}
