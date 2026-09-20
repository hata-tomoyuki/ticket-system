using System.Reflection;
using HelpDesk.Core;

namespace HelpDesk.Tests;

/// <summary>
/// 依存の向きが崩れていないことを検査するテスト。
/// 人間のレビューは見落とすが、ビルドのたびに走るテストは見落とさない。
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(CoreAssemblyMarker).Assembly;

    [Fact]
    public void Core_は_AspNetCore_に依存しない()
    {
        var violations = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Core_は_EntityFrameworkCore_に依存しない()
    {
        var violations = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(violations);
    }
}
