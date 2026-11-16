using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PizzaOps.Core.Interfaces;
using PizzaOps.DemoAdmin;
using PizzaOps.DemoAdmin.Commands;
using PizzaOps.DemoAdmin.Commands.Reset;
using PizzaOps.DemoAdmin.Commands.Status;

var services = new ServiceCollection();
services.AddSingleton<IAdminCommandService, AdminCommandService>();
services.AddSingleton<ResetCommand>();
services.AddSingleton<StatusCommand>();
using var provider = services.BuildServiceProvider();

var parseResult = RootCommandFactory.Create(provider).Parse(args);
var exitCode = await parseResult.InvokeAsync();
return exitCode != 0 && parseResult.Errors.Count > 0 ? 2 : exitCode;
