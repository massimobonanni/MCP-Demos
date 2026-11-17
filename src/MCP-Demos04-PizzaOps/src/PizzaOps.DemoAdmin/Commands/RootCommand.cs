using System.CommandLine;
using PizzaOps.DemoAdmin.Commands.Reset;
using PizzaOps.DemoAdmin.Commands.Status;
using PizzaOps.DemoAdmin.Extensions;

namespace PizzaOps.DemoAdmin.Commands;

internal static class RootCommandFactory
{
    public static RootCommand Create(IServiceProvider services)
    {
        var root = new RootCommand("PizzaOps operator maintenance and status.");
        root.Subcommands.Add(services.GetCommand<ResetCommand>());
        root.Subcommands.Add(services.GetCommand<StatusCommand>());
        return root;
    }
}
