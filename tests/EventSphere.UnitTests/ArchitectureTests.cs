using EventSphere.Domain;

namespace EventSphere.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_infrastructure_dependencies()
    {
        var references = typeof(AggregateRoot).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, x => x.Contains("EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.Contains("Confluent", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.Contains("AspNetCore", StringComparison.Ordinal));
    }
}
