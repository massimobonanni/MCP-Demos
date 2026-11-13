namespace System.Text.Json;

/// <summary>
/// Provides extension methods for reading JSON elements.
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>
    /// Gets a string-valued property using a case-insensitive property-name comparison.
    /// </summary>
    /// <param name="element">The JSON object to search.</param>
    /// <param name="propertyName">The name of the property to read.</param>
    /// <returns>The string value, or null if no matching string-valued property exists.</returns>
    /// <exception cref="InvalidOperationException">The element is not a JSON object.</exception>
    public static string? GetStringProperty(this JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
