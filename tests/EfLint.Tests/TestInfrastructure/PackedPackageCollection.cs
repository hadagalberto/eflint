using Xunit;

namespace EfLint.Tests.TestInfrastructure;

/// <summary>
/// The two tests that compile a consumer against the packed nupkg are serialized:
/// concurrent <c>dotnet pack</c> invocations contend for the SDK's NuGet/MSBuild
/// locks and can stall for many minutes on shared build agents.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackedPackageCollection
{
    public const string Name = "Packed package";
}
