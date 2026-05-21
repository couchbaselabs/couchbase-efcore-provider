using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Update.Internal;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Integration tests for <see cref="CouchbaseUpdateSqlGenerator"/>.
///
/// These tests exercise the component in its live deployment context — with all
/// Couchbase services wired into the DI container — rather than with hand-built mocks.
/// They verify two things:
///
///   1. DI wiring: <see cref="IUpdateSqlGenerator"/> resolves to the correct concrete
///      type after the full service registration runs.
///
///   2. SQL shape: the generator — as resolved through real DI — produces valid SQL++
///      syntax for sequence operations, confirming that the real
///      <see cref="CouchbaseSqlGenerationHelper"/> (not a mock) is
///      injected and used for identifier quoting.
///
/// Note: <see cref="CouchbaseDatabaseWrapper"/> handles SaveChanges via the
/// Couchbase document SDK directly (it does not call the SQL generator). Sequence execution
/// end-to-end is covered by <c>SequenceValueGenerationTests</c>.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class UpdateSqlGeneratorIntegrationTests(BloggingFixture fixture, ITestOutputHelper output)
{
    // -----------------------------------------------------------------------
    // DI wiring
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that <see cref="IUpdateSqlGenerator"/> resolves to
    /// <see cref="CouchbaseUpdateSqlGenerator"/> from <c>Update.Internal</c>.
    /// Regression guard: before the fix, the empty stub in <c>Storage.Internal</c>
    /// was silently resolved instead.
    /// </summary>
    [Fact]
    public async Task IUpdateSqlGenerator_ResolvesToCouchbaseUpdateSqlGeneratorFromUpdateInternal()
    {
        await using var context = fixture.GetDbContext();

        var generator = context.GetService<IUpdateSqlGenerator>();

        Assert.NotNull(generator);
        Assert.IsType<CouchbaseUpdateSqlGenerator>(generator);
        Assert.Equal(
            "Couchbase.EntityFrameworkCore.Update.Internal",
            generator.GetType().Namespace);

        output.WriteLine($"Resolved: {generator.GetType().FullName}");
    }

    // -----------------------------------------------------------------------
    // SQL shape — using the real DI-resolved generator
    //
    // These tests validate that the generator injected by the live DI container
    // produces the expected SQL++ syntax.  They differ from the unit tests in
    // that here the real CouchbaseSqlGenerationHelper (not a mock) handles
    // identifier quoting — catching any wiring mismatch between the two classes.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateNextSequenceValueOperation_WithSchema_ProducesSqlPlusPlusSyntax()
    {
        await using var context = fixture.GetDbContext();
        var generator = (CouchbaseUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        var sql = generator.GenerateNextSequenceValueOperation("order_seq", schema: "myScope");

        output.WriteLine($"Generated: {sql.Trim()}");
        Assert.Equal("SELECT NEXT VALUE FOR `myScope`.`order_seq`;", sql.Trim());
    }

    [Fact]
    public async Task GenerateNextSequenceValueOperation_WithoutSchema_ProducesUnqualifiedIdentifier()
    {
        await using var context = fixture.GetDbContext();
        var generator = (CouchbaseUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        var sql = generator.GenerateNextSequenceValueOperation("order_seq", schema: null);

        output.WriteLine($"Generated: {sql.Trim()}");
        Assert.Equal("SELECT NEXT VALUE FOR `order_seq`;", sql.Trim());
    }

    [Fact]
    public async Task GenerateNextSequenceValueOperation_IdentifierWithBacktick_EscapesCorrectly()
    {
        await using var context = fixture.GetDbContext();
        var generator = (CouchbaseUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        var sql = generator.GenerateNextSequenceValueOperation("my`seq", schema: null);

        output.WriteLine($"Generated: {sql.Trim()}");
        // Backtick inside identifier must be doubled by CouchbaseSqlGenerationHelper
        Assert.Contains("`my``seq`", sql);
    }

    [Fact]
    public async Task GenerateObtainNextSequenceValueOperation_ProducesIdenticalSqlAsGenerate()
    {
        await using var context = fixture.GetDbContext();
        var generator = (CouchbaseUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        var next = generator.GenerateNextSequenceValueOperation("order_seq", "myScope");
        var obtain = generator.GenerateObtainNextSequenceValueOperation("order_seq", "myScope");

        Assert.Equal(next, obtain);
    }

    [Fact]
    public async Task AppendStoredProcedureCall_ThrowsNotSupportedException()
    {
        await using var context = fixture.GetDbContext();
        var generator = context.GetService<IUpdateSqlGenerator>();

        // The method throws before touching the command argument.
        Assert.Throws<NotSupportedException>(() =>
            generator.AppendStoredProcedureCall(
                new System.Text.StringBuilder(),
                null!,
                0,
                out _));
    }

    // -----------------------------------------------------------------------
    // DI registration regression guards for services changed in recent work
    // -----------------------------------------------------------------------

    /// <summary>
    /// Regression guard: before the DI override fix, TryAdd semantics caused EF Core's
    /// core registration of RelationalDatabase to win over CouchbaseDatabaseWrapper,
    /// which routed all SaveChanges through the SQL generator and broke INSERT operations.
    /// </summary>
    [Fact]
    public async Task IDatabase_ResolvesToCouchbaseDatabaseWrapper_NotRelationalDatabase()
    {
        await using var context = fixture.GetDbContext();

        var database = context.GetService<IDatabase>();

        Assert.NotNull(database);
        Assert.IsType<CouchbaseDatabaseWrapper>(database);
        output.WriteLine($"Resolved: {database.GetType().FullName}");
    }

    /// <summary>
    /// Verifies that <see cref="IModificationCommandBatchFactory"/> resolves to the
    /// Couchbase implementation from <c>Update.Internal</c> (not a stub or default).
    /// </summary>
    [Fact]
    public async Task IModificationCommandBatchFactory_ResolvesToCouchbaseFactory()
    {
        await using var context = fixture.GetDbContext();

        var factory = context.GetService<IModificationCommandBatchFactory>();

        Assert.NotNull(factory);
        Assert.IsType<CouchbaseModificationCommandBatchFactory>(factory);
        output.WriteLine($"Resolved: {factory.GetType().FullName}");
    }

    /// <summary>
    /// Verifies that <see cref="IValueGeneratorSelector"/> resolves to
    /// <see cref="CouchbaseValueGeneratorSelector"/> after the DI override fix.
    /// </summary>
    [Fact]
    public async Task IValueGeneratorSelector_ResolvesToCouchbaseValueGeneratorSelector()
    {
        await using var context = fixture.GetDbContext();

        var selector = context.GetService<IValueGeneratorSelector>();

        Assert.NotNull(selector);
        Assert.IsType<CouchbaseValueGeneratorSelector>(selector);
        output.WriteLine($"Resolved: {selector.GetType().FullName}");
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
