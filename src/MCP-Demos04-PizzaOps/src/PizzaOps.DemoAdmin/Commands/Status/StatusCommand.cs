using System.CommandLine;
using PizzaOps.Core.Interfaces;

namespace PizzaOps.DemoAdmin.Commands.Status;

internal sealed class StatusCommand : CommandBase
{
    public StatusCommand(IAdminCommandService service)
        : base("status", "Read the selected kitchen's status.", service)
    {
        this.SetAction(CommandHandler);
    }

    private async Task<int> CommandHandler(ParseResult parseResult, CancellationToken cancellationToken) =>
        await _service.StatusAsync(parseResult.GetRequiredValue(_environmentOption), cancellationToken);
}
