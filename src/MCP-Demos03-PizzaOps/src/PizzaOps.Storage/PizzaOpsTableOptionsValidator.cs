using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PizzaOps.Storage;

/// <summary>Validates that local and Azure credentials cannot be selected interchangeably.</summary>
public sealed partial class PizzaOpsTableOptionsValidator(string environmentName)
    : IValidateOptions<PizzaOpsTableOptions>
{
    private static readonly string[] ForbiddenPartitionCharacters = ["#", "/", "\\", "?"];

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PizzaOpsTableOptions options)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return ValidateOptionsResult.Fail("An application environment name is required.");
        }

        if (!IsValidTableName(options.OvensTableName) || !IsValidTableName(options.EventsTableName))
        {
            return ValidateOptionsResult.Fail("Table names must be 3-63 alphanumeric characters and start with a letter.");
        }

        if (string.Equals(options.OvensTableName, options.EventsTableName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail("The ovens and events tables must be different.");
        }

        if (string.IsNullOrWhiteSpace(options.PartitionKey) ||
            options.PartitionKey.Length > 1024 ||
            options.PartitionKey.Any(char.IsControl) ||
            ForbiddenPartitionCharacters.Any(options.PartitionKey.Contains))
        {
            return ValidateOptionsResult.Fail("The kitchen partition key is invalid.");
        }

        if (string.Equals(options.Mode, "Azurite", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Fail("Azurite is allowed only in the Development environment.");
            }

            if (!string.Equals(options.AzuriteConnectionString, "UseDevelopmentStorage=true", StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Fail("Azurite requires the explicit UseDevelopmentStorage=true connection string.");
            }

            if (!string.IsNullOrWhiteSpace(options.TableEndpoint) ||
                !string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
            {
                return ValidateOptionsResult.Fail("Azurite cannot be combined with Azure endpoint or managed identity settings.");
            }

            return ValidateOptionsResult.Success;
        }

        if (string.Equals(options.Mode, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Fail("Development must explicitly use Azurite.");
            }

            if (!string.IsNullOrWhiteSpace(options.AzuriteConnectionString))
            {
                return ValidateOptionsResult.Fail("Azure mode does not accept a connection string.");
            }

            if (!Uri.TryCreate(options.TableEndpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
            {
                return ValidateOptionsResult.Fail("Azure mode requires an absolute HTTPS Table endpoint.");
            }

            if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) &&
                !Guid.TryParse(options.ManagedIdentityClientId, out _))
            {
                return ValidateOptionsResult.Fail("The managed identity client ID must be a GUID.");
            }

            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail("Mode must be explicitly set to Azurite or Azure.");
    }

    private static bool IsValidTableName(string? value) =>
        value is not null && TableNamePattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TableNamePattern();
}
