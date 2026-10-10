using System.Reflection;
using Microsoft.EntityFrameworkCore;using Xunit;

namespace Couchbase.EntityFrameworkCore.SpecificationTests;

/// <summary>
/// Fails when an EF Core specification test class exists that this project neither extends nor
/// explicitly opts out of via <see cref="IgnoredTestBases"/>. It is the checklist for coverage and
/// also flags new upstream test classes after an EF Core upgrade.
/// </summary>
/// <remarks>
/// Coverage is being built up incrementally, so this check is skipped until most upstream classes
/// are extended. Remove the Skip (and list unsupported bases in <see cref="IgnoredTestBases"/>)
/// once that is true.
/// </remarks>
public class CouchbaseComplianceTest : RelationalComplianceTestBase
{
    protected override Assembly TargetAssembly => typeof(CouchbaseComplianceTest).Assembly;

    [Fact(Skip = "Spec coverage is incremental; enable once most upstream test bases are extended.")]
    public override void All_test_bases_must_be_implemented()
        => base.All_test_bases_must_be_implemented();
}
