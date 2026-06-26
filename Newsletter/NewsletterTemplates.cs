namespace CherryBox.Newsletter.Plugin;

internal sealed record SkinTheme(
    string Label,
    string PrimaryColor,
    string SecondaryColor,
    string BackgroundColor,
    string TextColor,
    string? HeaderGradient = null);

internal static class NewsletterTemplates
{
    public static SkinTheme GetTheme(string? skinId)
    {
        return NormalizeSkinId(skinId) switch
        {
            "boy" => new SkinTheme("Boy", "#2563eb", "#eff6ff", "#f8fafc", "#0f172a"),
            "trans" => new SkinTheme("Trans", "#9b59b6", "#f3e8ff", "#faf5ff", "#312e81"),
            "pride" => new SkinTheme(
                "Pride",
                "#e40303",
                "#fff7ed",
                "#fffbeb",
                "#1f2937",
                "linear-gradient(90deg, #e40303, #ff8c00, #ffed00, #008026, #004dff, #750787)"),
            "girl" => new SkinTheme("Girl", "#ff69b4", "#fff0f5", "#fffafc", "#4a1942"),
            _ => new SkinTheme("CherryBox", "#64748b", "#f1f5f9", "#f8fafc", "#1e293b")
        };
    }

    public static string RenderWelcome(string username, string baseUrl, SkinTheme theme)
    {
        var safeName = Escape(username);
        var safeUrl = Escape(baseUrl.TrimEnd('/'));
        var headerStyle = theme.HeaderGradient is null
            ? $"background:{theme.PrimaryColor};"
            : $"background:{theme.HeaderGradient};";

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>Welcome to CherryBox</title></head>
            <body style="margin:0;padding:0;background:{theme.BackgroundColor};font-family:Segoe UI,Arial,sans-serif;color:{theme.TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:{theme.BackgroundColor};padding:24px 0;">
                <tr><td align="center">
                  <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(0,0,0,0.08);">
                    <tr><td style="{headerStyle}color:#ffffff;padding:28px 32px;">
                      <h1 style="margin:0;font-size:28px;">Welcome to CherryBox</h1>
                      <p style="margin:8px 0 0;opacity:0.95;">{Escape(theme.Label)} theme</p>
                    </td></tr>
                    <tr><td style="padding:32px;">
                      <p style="font-size:18px;margin:0 0 16px;">Hi {safeName},</p>
                      <p style="line-height:1.6;margin:0 0 16px;">Your CherryBox account is ready — your private stash of filth, waiting whenever you're hard and need to cum. Sign in, pull up your favorites, and stroke to whatever makes you throb.</p>
                      <p style="margin:24px 0;">
                        <a href="{safeUrl}" style="display:inline-block;background:{theme.PrimaryColor};color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:999px;font-weight:600;">Open CherryBox</a>
                      </p>
                      <p style="line-height:1.6;margin:0;color:#64748b;font-size:14px;">You can update your email or newsletter preferences anytime from your Account settings.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    public static string RenderWeeklyDigest(
        string username,
        string baseUrl,
        SkinTheme theme,
        IReadOnlyList<NewsletterDigestItem> items,
        string? aiIntro = null)
    {
        var safeName = Escape(username);
        var safeUrl = Escape(baseUrl.TrimEnd('/'));
        var headerStyle = theme.HeaderGradient is null
            ? $"background:{theme.PrimaryColor};"
            : $"background:{theme.HeaderGradient};";

        var introHtml = BuildIntroHtml(username, aiIntro);
        var listHeading = string.IsNullOrWhiteSpace(aiIntro)
            ? "Fresh filth added this week — pick something and get off:"
            : "Here's what I want you to stroke to:";

        var itemRows = items.Count == 0
            ? "<p style=\"margin:0;line-height:1.6;\">Nothing new dropped this week, but your library is still packed with stuff to cum to. Open CherryBox and find what makes you leak.</p>"
            : string.Join("", items.Select(RenderItemRow));

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>Your CherryBox weekly update</title></head>
            <body style="margin:0;padding:0;background:{theme.BackgroundColor};font-family:Segoe UI,Arial,sans-serif;color:{theme.TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:{theme.BackgroundColor};padding:24px 0;">
                <tr><td align="center">
                  <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(0,0,0,0.08);">
                    <tr><td style="{headerStyle}color:#ffffff;padding:28px 32px;">
                      <h1 style="margin:0;font-size:28px;">This week in CherryBox</h1>
                      <p style="margin:8px 0 0;opacity:0.95;">Fresh picks for {safeName}</p>
                    </td></tr>
                    <tr><td style="padding:32px;">
                      {introHtml}
                      <p style="line-height:1.6;margin:0 0 20px;">{Escape(listHeading)}</p>
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{itemRows}</table>
                      <p style="margin:28px 0 0;">
                        <a href="{safeUrl}" style="display:inline-block;background:{theme.PrimaryColor};color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:999px;font-weight:600;">Browse CherryBox</a>
                      </p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        string RenderItemRow(NewsletterDigestItem item)
        {
            var metaParts = new List<string> { item.MediaType, item.UpdatedAt.ToString("MMM d, yyyy") };
            if (!string.IsNullOrWhiteSpace(item.Performers))
                metaParts.Add(item.Performers);
            if (!string.IsNullOrWhiteSpace(item.Studio))
                metaParts.Add(item.Studio);
            if (!string.IsNullOrWhiteSpace(item.Tags))
                metaParts.Add(item.Tags);

            return $"""
                <tr>
                  <td style="padding:12px 0;border-bottom:1px solid {theme.SecondaryColor};">
                    <a href="{Escape(item.Url)}" style="color:{theme.PrimaryColor};font-weight:600;text-decoration:none;">{Escape(item.Title)}</a>
                    <div style="font-size:13px;color:#64748b;margin-top:4px;">{Escape(string.Join(" · ", metaParts))}</div>
                  </td>
                </tr>
                """;
        }
    }

    public static string WelcomePlainText(string username, string baseUrl) =>
        $"""
        Hi {username},

        Welcome to CherryBox! Your account is ready — your private porn stash is waiting.

        Open CherryBox and stroke to whatever gets you hardest: {baseUrl.TrimEnd('/')}

        You can update your email or newsletter preferences from Account settings.
        """;

    public static string WeeklyPlainText(
        string username,
        string baseUrl,
        IReadOnlyList<NewsletterDigestItem> items,
        string? aiIntro = null)
    {
        var intro = BuildIntroPlain(username, aiIntro);
        var lines = items.Count == 0
            ? ["Nothing new this week — open CherryBox anyway and find something to cum to."]
            : items.Select(FormatItemPlain).ToArray();

        return $"""
            {intro}

            {string.Join(Environment.NewLine, lines)}

            Browse CherryBox: {baseUrl.TrimEnd('/')}
            """;
    }

    private static string BuildIntroHtml(string username, string? aiIntro)
    {
        if (string.IsNullOrWhiteSpace(aiIntro))
        {
            return $"""
                <p style="font-size:18px;margin:0 0 16px;">Hi {Escape(username)},</p>
                """;
        }

        var personalized = PersonalizeIntro(aiIntro, username);
        var paragraphs = personalized
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => $"""<p style="line-height:1.7;margin:0 0 16px;">{Escape(p)}</p>""");

        return string.Join("", paragraphs);
    }

    private static string BuildIntroPlain(string username, string? aiIntro)
    {
        if (string.IsNullOrWhiteSpace(aiIntro))
            return $"Hi {username},\n\nYour weekly dose of filth from CherryBox:";

        return PersonalizeIntro(aiIntro, username);
    }

    private static string PersonalizeIntro(string aiIntro, string username) =>
        aiIntro.Replace("[NAME]", username, StringComparison.OrdinalIgnoreCase);

    private static string FormatItemPlain(NewsletterDigestItem item)
    {
        var details = new List<string> { item.MediaType };
        if (!string.IsNullOrWhiteSpace(item.Performers))
            details.Add(item.Performers);
        if (!string.IsNullOrWhiteSpace(item.Studio))
            details.Add(item.Studio);
        if (!string.IsNullOrWhiteSpace(item.Tags))
            details.Add(item.Tags);
        return $"- {item.Title} ({string.Join(", ", details)}) {item.Url}";
    }

    private static string NormalizeSkinId(string? skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
            return "girl";

        var normalized = skinId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "boy" or "trans" or "pride" or "girl" => normalized,
            _ => "other"
        };
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
