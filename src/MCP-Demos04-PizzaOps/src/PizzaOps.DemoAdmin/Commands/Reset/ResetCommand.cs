using System.CommandLine;
using PizzaOps.Core.Interfaces;

namespace PizzaOps.DemoAdmin.Commands.Reset;

internal sealed class ResetCommand : CommandBase
{
    private readonly Option<bool> _quiescedOption;

    public ResetCommand(IAdminCommandService service)
        : base("reset", "Reset the selected kitchen during a maintenance window.", service)
    {
        _quiescedOption = new Option<bool>("--quiesced")
        {
            Description = "Acknowledge that tool calls are paused and in-flight calls have drained.",
            Required = true
        };
        _quiescedOption.Aliases.Add("-q");
        _quiescedOption.Validators.Add(result =>
        {
            if (result.IdentifierTokenCount > 1)
            {
                result.AddError("--quiesced must be specified only once.");
            }
        });
        Options.Add(_quiescedOption);
        Validators.Add(result =>
        {
            if (!result.GetValue(_quiescedOption))
            {
                result.AddError("Reset requires --quiesced to acknowledge paused tool calls and drained in-flight calls.");
            }
        });
        this.SetAction(CommandHandler);
    }

    private async Task<int> CommandHandler(ParseResult parseResult, CancellationToken cancellationToken) =>
        await _service.ResetAsync(parseResult.GetRequiredValue(_environmentOption), cancellationToken);
}
