using System.Reflection;

namespace App.UnitTests;

/// <summary>Enforces the dependency rule: Domain ← Application ← Infrastructure ← Api.</summary>
public class ArchitectureTests
{
    private static string[] AppReferences(string assembly) =>
        Assembly.Load(assembly).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("App.", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Domain_depends_on_no_other_layer() =>
        Assert.Empty(AppReferences("App.Domain"));

    [Fact]
    public void Application_depends_only_on_Domain() =>
        Assert.All(AppReferences("App.Application"), n => Assert.Equal("App.Domain", n));

    [Fact]
    public void Infrastructure_does_not_depend_on_Api() =>
        Assert.DoesNotContain("App.Api", AppReferences("App.Infrastructure"));
}
