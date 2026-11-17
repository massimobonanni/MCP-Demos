using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PizzaOps.Dashboard.Presentation;

/// <summary>Reports transport lifecycle without introducing a second connection.</summary>
public sealed class DashboardCircuit : CircuitHandler
{
    /// <summary>Whether the circuit currently has a browser connection.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Notifies the page to cancel reads or refresh after reconnection.</summary>
    public event Action<bool>? Changed;

    /// <inheritdoc />
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        IsConnected = true;
        Changed?.Invoke(true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        IsConnected = false;
        Changed?.Invoke(false);
        return Task.CompletedTask;
    }
}
