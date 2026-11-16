using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PizzaOps.Core.Interfaces;
using PizzaOps.DemoAdmin.Commands;
using PizzaOps.DemoAdmin.Commands.Reset;
using PizzaOps.DemoAdmin.Commands.Status;

namespace PizzaOps.UnitTests;

public sealed class AdminCommandTests
{
    [Theory]
    [InlineData("reset")]
    [InlineData("reset", "--environment", "demo")]
    [InlineData("reset", "--environment", "demo", "--quiesced=false")]
    [InlineData("reset", "--environment", "../demo", "--quiesced")]
    [InlineData("reset", "--environment", "", "--quiesced")]
    [InlineData("reset", "--environment", "123456789012345678901234567890123", "--quiesced")]
    [InlineData("reset", "--environment", "demo", "--quiesced", "--quiesced")]
    [InlineData("reset", "-e", "demo", "-q", "--quiesced")]
    [InlineData("reset", "--environment", "demo", "--environment", "other", "--quiesced")]
    [InlineData("reset", "-e", "demo", "--environment", "other", "-q")]
    [InlineData("status")]
    [InlineData("status", "--environment", "demo", "--quiesced")]
    [InlineData("status", "--environment", "demo", "--unknown")]
    public async Task InvalidCommandsNeverInvokeTheHandler(params string[] args)
    {
        var service = new FakeAdminCommandService();
        using var provider = CreateProvider(service);
        var root = RootCommandFactory.Create(provider);

        var result = root.Parse(args);
        Assert.NotEmpty(result.Errors);
        await result.InvokeAsync();
        Assert.Null(service.Command);
    }

    [Theory]
    [InlineData("status", "demo", "status", "--environment", "demo")]
    [InlineData("reset", "Development", "reset", "--quiesced", "--environment", "Development")]
    [InlineData("reset", "Development", "reset", "-e", "Development", "-q")]
    [InlineData("status", "demo", "status", "-e", "demo")]
    public async Task ValidCommandsDispatchTheRequestedEnvironment(
        string expectedCommand,
        string expectedEnvironment,
        params string[] args)
    {
        var service = new FakeAdminCommandService();
        using var provider = CreateProvider(service);
        var root = RootCommandFactory.Create(provider);

        var result = root.Parse(args);
        Assert.Empty(result.Errors);
        Assert.Equal(7, await result.InvokeAsync());
        Assert.Equal(expectedCommand, service.Command);
        Assert.Equal(expectedEnvironment, service.Environment);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("reset", "--help")]
    public async Task HelpDoesNotInvokeTheHandler(params string[] args)
    {
        var service = new FakeAdminCommandService();
        using var provider = CreateProvider(service);
        var root = RootCommandFactory.Create(provider);

        Assert.Equal(0, await root.Parse(args).InvokeAsync());
        Assert.Null(service.Command);
    }

    private static ServiceProvider CreateProvider(IAdminCommandService service)
    {
        var services = new ServiceCollection();
        services.AddSingleton(service);
        services.AddSingleton<ResetCommand>();
        services.AddSingleton<StatusCommand>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeAdminCommandService : IAdminCommandService
    {
        public string? Command { get; private set; }

        public string? Environment { get; private set; }

        public Task<int> StatusAsync(string environmentName, CancellationToken cancellationToken) =>
            Dispatch("status", environmentName);

        public Task<int> ResetAsync(string environmentName, CancellationToken cancellationToken) =>
            Dispatch("reset", environmentName);

        private Task<int> Dispatch(string command, string environmentName)
        {
            Command = command;
            Environment = environmentName;
            return Task.FromResult(7);
        }
    }
}
