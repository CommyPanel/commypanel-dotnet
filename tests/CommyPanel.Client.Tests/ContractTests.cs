using System.Reflection;
using System.Text.RegularExpressions;

namespace CommyPanel.Tests;

/// <summary>
/// testdata/openapi.yaml is a copy of the API description in the panel repository (scripts/sync-spec.sh). These tests
/// fail when the API gains a call this library does not have, or an ability it does not know, so the library cannot
/// quietly fall behind.
/// </summary>
public class ContractTests
{
    // The client method that implements each operation of the spec.
    private static readonly Dictionary<string, string> MethodOf = new()
    {
        ["getToken"] = "TokenAsync",
        ["listApps"] = "ListAppsAsync",
        ["getApp"] = "GetAppAsync",
        ["listDeployments"] = "ListDeploymentsAsync",
        ["startDeployment"] = "StartDeploymentAsync",
        ["getDeployment"] = "GetDeploymentAsync",
        ["applyChanges"] = "ApplyChangesAsync",
        ["startApp"] = "StartAppAsync",
        ["stopApp"] = "StopAppAsync",
        ["restartApp"] = "RestartAppAsync",
        ["getLogs"] = "LogsAsync",
        ["listEnv"] = "ListEnvAsync",
        ["setEnv"] = "SetEnvAsync",
        ["unsetEnv"] = "UnsetEnvAsync",
    };

    private static string Spec() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "openapi.yaml"));

    private static HashSet<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet();

    [Fact]
    public void EveryOperationOfTheSpecHasAMethod()
    {
        var ids = Regex.Matches(Spec(), @"^\s+operationId:\s*(\w+)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        Assert.True(ids.Count >= 14, $"found only {ids.Count} operations in the spec");

        foreach (var id in ids)
        {
            Assert.True(MethodOf.TryGetValue(id, out var name), $"the spec has the operation '{id}' and this library has no method for it");
            Assert.True(typeof(CommyPanelClient).GetMethod(name!) is not null, $"operation '{id}' should be CommyPanelClient.{name}, which does not exist");
        }

        foreach (var id in MethodOf.Keys)
        {
            Assert.True(ids.Contains(id), $"the library maps '{id}', which is no longer in the spec");
        }
    }

    [Fact]
    public void TheSpecsAbilitiesAreTheLibrarys()
    {
        var known = Constants(typeof(Abilities));
        var used = Regex.Matches(Spec(), @"x-ability:\s*['""]?([\w:]+)['""]?").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.NotEmpty(used);

        foreach (var ability in used)
        {
            Assert.True(known.Contains(ability), $"the spec uses the ability '{ability}', which the library does not define");
        }

        Assert.True(known.SetEquals(used), "the library defines abilities the spec does not use: " + string.Join(", ", known.Except(used)));
    }

    [Fact]
    public void TheSpecsDeployModesAreTheLibrarys()
    {
        var text = Spec();

        foreach (var mode in Constants(typeof(DeployStrategies)))
        {
            Assert.True(Regex.IsMatch(text, @"^\s+-\s*" + Regex.Escape(mode) + @"\s*$", RegexOptions.Multiline), $"deploy mode '{mode}' is not in the spec");
        }
    }
}
