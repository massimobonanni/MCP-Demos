using System.CommandLine;
using PizzaOps.Core.Interfaces;

namespace PizzaOps.DemoAdmin.Commands;

internal abstract class CommandBase : Command
{
    protected readonly Option<string> _environmentOption;
    protected readonly IAdminCommandService _service;

    protected CommandBase(string name, string description, IAdminCommandService service)
        : base(name, description)
    {
        _service = service;
        _environmentOption = new Option<string>("--environment")
        {
            Description = "Explicit environment name, used to select the configuration profile.",
            Required = true
        };
        _environmentOption.Aliases.Add("-e");
        _environmentOption.Validators.Add(result =>
        {
            var value = result.GetValue(_environmentOption);
            if (result.IdentifierTokenCount > 1 ||
                value is null ||
                value.Length is 0 or > 32 ||
                !char.IsAsciiLetterOrDigit(value[0]) ||
                value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            {
                result.AddError(
                    "--environment must occur once and be 1-32 ASCII letters, digits, hyphens or underscores, starting with a letter or digit.");
            }
        });
        Options.Add(_environmentOption);
    }
}
