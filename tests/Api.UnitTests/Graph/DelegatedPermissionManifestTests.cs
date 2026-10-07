using System.Reflection;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class DelegatedPermissionManifestTests
{
    private const string GraphApplicationId = "00000003-0000-0000-c000-000000000000";

    [Fact]
    public void Manifest_matches_catalog_with_only_graph_delegated_scopes()
    {
        var path = FindRepositoryRoot();
        var manifestPath = Path.Combine(path, "infra", "entra", "delegated-permissions.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var accessBlocks = document.RootElement.GetProperty("requiredResourceAccess");

        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("requiredResourceAccess");
        accessBlocks.GetArrayLength().Should().Be(1);
        var graphBlock = accessBlocks[0];
        graphBlock.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("resourceAppId", "resourceAccess");
        graphBlock.GetProperty("resourceAppId").GetString().Should().Be(GraphApplicationId);

        var permissions = graphBlock.GetProperty("resourceAccess")
            .EnumerateArray()
            .Select(permission => new
            {
                Name = permission.GetProperty("name").GetString()!,
                Type = permission.GetProperty("type").GetString()!
            })
            .ToArray();
        var scopeNames = permissions.Select(permission => permission.Name).ToArray();
        graphBlock.GetProperty("resourceAccess").EnumerateArray()
            .SelectMany(permission => permission.EnumerateObject().Select(property => property.Name))
            .Should().OnlyContain(name => name == "name" || name == "type");
        var expected = GraphScopeCatalog.CapabilityEvaluationScopes
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        scopeNames.Should().Equal(expected);
        permissions.Should().OnlyContain(permission => permission.Type == "Scope");

        var operationScopes = typeof(GraphScopeCatalog)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(IReadOnlyList<string>))
            .SelectMany(field => (IReadOnlyList<string>)field.GetValue(null)!)
            .Distinct(StringComparer.Ordinal);
        operationScopes.Should().OnlyContain(scope => scopeNames.Contains(scope, StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
