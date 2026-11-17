namespace PizzaOps.DemoAdmin;

/// <summary>Non-secret optional settings for the DemoAdmin command output.</summary>
public sealed class PizzaOpsAdminOptions
{
    /// <summary>Configuration section for operator-only settings.</summary>
    public const string SectionName = "PizzaOps:Admin";

    /// <summary>Optional audience dashboard URL.</summary>
    public string? DashboardUrl { get; set; }

    /// <summary>Rejects dashboard URLs that could expose credentials or unsafe schemes.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DashboardUrl))
        {
            DashboardUrl = null;
            return;
        }

        if (!Uri.TryCreate(DashboardUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "DashboardUrl must be an absolute HTTP(S) URL without user info, query, or fragment.");
        }

        DashboardUrl = uri.ToString().TrimEnd('/');
    }
}
