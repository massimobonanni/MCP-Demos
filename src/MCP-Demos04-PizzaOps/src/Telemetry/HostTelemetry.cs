using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace PizzaOps.Telemetry;

internal sealed class RoleNameInitializer(string roleName) : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry) => telemetry.Context.Cloud.RoleName = roleName;
}

internal sealed class SafeTelemetryProcessor(ITelemetryProcessor next) : ITelemetryProcessor
{
    public void Process(ITelemetry item)
    {
        if (item is RequestTelemetry request)
        {
            if (request.Url is { } url)
            {
                request.Url = new Uri(url.GetLeftPart(UriPartial.Path));
            }

            // Suppress only successful polling requests, never their warnings or failures.
            if (request.Success == true && request.ResponseCode == "200" &&
                request.Url?.AbsolutePath is "/api/kitchen/state" or "/health/live" or "/health/ready")
            {
                return;
            }
        }

        if (item is DependencyTelemetry dependency &&
            Uri.TryCreate(dependency.Data, UriKind.Absolute, out var dependencyUrl))
        {
            dependency.Data = dependencyUrl.GetLeftPart(UriPartial.Path);
        }

        if (item is DependencyTelemetry
            { Name: "PizzaOps.dashboard.refresh", Success: true, ResultCode: "OK" })
        {
            return;
        }

        next.Process(item);
    }
}
