using System.Text;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Update.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseUpdateSqlGeneratorTests
{
    // -----------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------

    private readonly CouchbaseUpdateSqlGenerator _generator;

    public CouchbaseUpdateSqlGeneratorTests()
    {
        var helper = new CouchbaseSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
        var typeMappingSource = new Mock<IRelationalTypeMappingSource>().Object;
        _generator = new CouchbaseUpdateSqlGenerator(new UpdateSqlGeneratorDependencies(helper, typeMappingSource));
    }

    // -----------------------------------------------------------------------
    // INSERT
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendInsertOperation_WithReadColumns_EmitsInsertWithReturningClause()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: null,
            WriteCol("name", "p0"),
            ReadCol("id"));

        _generator.AppendInsertOperation(sb, command, 0, out _);

        // CouchbaseSqlGenerationHelper.GenerateParameterName prepends "$" to the raw name.
        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("INSERT INTO `airline` (`name`)", sql);
        Assert.Contains("VALUES ($p0)", sql);
        Assert.Contains("RETURNING `id`", sql);
        Assert.EndsWith(";", sql);
    }

    [Fact]
    public void AppendInsertOperation_WithoutReadColumns_EmitsPlainInsertWithoutReturning()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: null,
            WriteCol("name", "p0"),
            WriteCol("country", "p1"));

        _generator.AppendInsertOperation(sb, command, 0, out _);

        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("INSERT INTO `airline` (`name`, `country`)", sql);
        Assert.DoesNotContain("RETURNING", sql);
        Assert.EndsWith(";", sql);
    }

    [Fact]
    public void AppendInsertOperation_WithSchema_DelimitsSchemaAndCollection()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: "inventory",
            WriteCol("name", "p0"),
            ReadCol("id"));

        _generator.AppendInsertOperation(sb, command, 0, out _);

        Assert.Contains("INSERT INTO `inventory`.`airline`", NormalizeSql(sb.ToString()));
    }

    [Fact]
    public void AppendInsertOperation_WithReadColumns_ReturnsLastInResultSet()
    {
        var command = BuildCommand("airline", null,
            WriteCol("name", "p0"),
            ReadCol("id"));

        var result = _generator.AppendInsertOperation(new StringBuilder(), command, 0, out var requiresTransaction);

        Assert.True(result.HasFlag(ResultSetMapping.LastInResultSet));
        Assert.False(requiresTransaction);
    }

    [Fact]
    public void AppendInsertOperation_WithoutReadColumns_ReturnsNoResults()
    {
        var command = BuildCommand("airline", null, WriteCol("name", "p0"));

        var result = _generator.AppendInsertOperation(new StringBuilder(), command, 0, out _);

        Assert.Equal(ResultSetMapping.NoResults, result);
    }

    [Fact]
    public void AppendInsertOperation_MultipleWriteColumns_IncludesAllInValuesClause()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", null,
            WriteCol("id", "p0"),
            WriteCol("name", "p1"),
            WriteCol("country", "p2"),
            ReadCol("id"));

        _generator.AppendInsertOperation(sb, command, 0, out _);

        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("`id`, `name`, `country`", sql);
        Assert.Contains("$p0, $p1, $p2", sql);
    }

    // -----------------------------------------------------------------------
    // UPDATE
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendUpdateOperation_EmitsUpdateSetWhereReturning()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: null,
            WriteCol("name", "p0"),
            ConditionCol("id", "p1"),
            ReadCol("id"));

        _generator.AppendUpdateOperation(sb, command, 0, out _);

        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("UPDATE `airline`", sql);
        Assert.Contains("`name` = $p0", sql);
        Assert.Contains("WHERE `id` = $p1", sql);
        Assert.Contains("RETURNING `id`", sql);
        Assert.EndsWith(";", sql);
    }

    [Fact]
    public void AppendUpdateOperation_WithSchema_DelimitsSchemaAndCollection()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: "inventory",
            WriteCol("name", "p0"),
            ConditionCol("id", "p1"),
            ReadCol("id"));

        _generator.AppendUpdateOperation(sb, command, 0, out _);

        Assert.Contains("UPDATE `inventory`.`airline`", NormalizeSql(sb.ToString()));
    }

    [Fact]
    public void AppendUpdateOperation_ReturnsLastInResultSet()
    {
        var command = BuildCommand("airline", null,
            WriteCol("name", "p0"),
            ConditionCol("id", "p1"),
            ReadCol("id"));

        var result = _generator.AppendUpdateOperation(new StringBuilder(), command, 0, out var requiresTransaction);

        Assert.True(result.HasFlag(ResultSetMapping.LastInResultSet));
        Assert.False(requiresTransaction);
    }

    [Fact]
    public void AppendUpdateOperation_MultipleSetColumns_IncludesAllInSetClause()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", null,
            WriteCol("name", "p0"),
            WriteCol("country", "p1"),
            ConditionCol("id", "p2"),
            ReadCol("id"));

        _generator.AppendUpdateOperation(sb, command, 0, out _);

        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("`name` = $p0", sql);
        Assert.Contains("`country` = $p1", sql);
    }

    // -----------------------------------------------------------------------
    // DELETE
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendDeleteOperation_EmitsDeleteFromWhereReturning()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: null,
            ConditionCol("id", "p0"),
            ReadCol("id"));

        _generator.AppendDeleteOperation(sb, command, 0, out _);

        var sql = NormalizeSql(sb.ToString());
        Assert.Contains("DELETE FROM `airline`", sql);
        Assert.Contains("WHERE `id` = $p0", sql);
        // EF Core 10 UpdateSqlGenerator always emits RETURNING 1 for DELETE (row-count sentinel).
        Assert.Contains("RETURNING 1", sql);
        Assert.EndsWith(";", sql);
    }

    [Fact]
    public void AppendDeleteOperation_WithSchema_DelimitsSchemaAndCollection()
    {
        var sb = new StringBuilder();
        var command = BuildCommand("airline", schema: "inventory",
            ConditionCol("id", "p0"),
            ReadCol("id"));

        _generator.AppendDeleteOperation(sb, command, 0, out _);

        Assert.Contains("DELETE FROM `inventory`.`airline`", NormalizeSql(sb.ToString()));
    }

    [Fact]
    public void AppendDeleteOperation_DoesNotRequireTransaction()
    {
        var command = BuildCommand("airline", null,
            ConditionCol("id", "p0"),
            ReadCol("id"));

        _generator.AppendDeleteOperation(new StringBuilder(), command, 0, out var requiresTransaction);

        Assert.False(requiresTransaction);
    }

    [Fact]
    public void AppendDeleteOperation_WithoutReadColumns_EmitsReturningOneSentinel()
    {
        // EF Core 10 UpdateSqlGenerator always emits RETURNING 1 for DELETE so the
        // driver can read the affected-row count, even when there are no read columns.
        var sb = new StringBuilder();
        var command = BuildCommand("airline", null, ConditionCol("id", "p0"));

        _generator.AppendDeleteOperation(sb, command, 0, out _);

        Assert.Contains("RETURNING 1", NormalizeSql(sb.ToString()));
    }

    // -----------------------------------------------------------------------
    // Batch header / autocommit
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendBatchHeader_WritesNothing()
    {
        var sb = new StringBuilder();
        _generator.AppendBatchHeader(sb);
        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void PrependEnsureAutocommit_WritesNothing()
    {
        var sb = new StringBuilder();
        _generator.PrependEnsureAutocommit(sb);
        Assert.Equal(string.Empty, sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Stored procedure
    // -----------------------------------------------------------------------

    [Fact]
    public void AppendStoredProcedureCall_ThrowsNotSupportedException()
    {
        var command = new Mock<IReadOnlyModificationCommand>();
        Assert.Throws<NotSupportedException>(() =>
            _generator.AppendStoredProcedureCall(new StringBuilder(), command.Object, 0, out _));
    }

    // -----------------------------------------------------------------------
    // Sequence value operations
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerateNextSequenceValueOperation_WithoutSchema_EmitsSqlPlusPlusSyntax()
    {
        var sql = NormalizeSql(_generator.GenerateNextSequenceValueOperation("order_seq", schema: null));
        Assert.Equal("SELECT NEXT VALUE FOR `order_seq`;", sql);
    }

    [Fact]
    public void GenerateNextSequenceValueOperation_WithSchema_IncludesSchemaPrefix()
    {
        var sql = NormalizeSql(_generator.GenerateNextSequenceValueOperation("order_seq", schema: "myScope"));
        Assert.Equal("SELECT NEXT VALUE FOR `myScope`.`order_seq`;", sql);
    }

    [Fact]
    public void AppendNextSequenceValueOperation_WithoutSchema_EmitsSqlPlusPlusSyntax()
    {
        var sb = new StringBuilder();
        _generator.AppendNextSequenceValueOperation(sb, "order_seq", schema: null);
        Assert.Equal("SELECT NEXT VALUE FOR `order_seq`;", NormalizeSql(sb.ToString()));
    }

    [Fact]
    public void AppendNextSequenceValueOperation_WithSchema_IncludesSchemaPrefix()
    {
        var sb = new StringBuilder();
        _generator.AppendNextSequenceValueOperation(sb, "order_seq", schema: "myScope");
        Assert.Equal("SELECT NEXT VALUE FOR `myScope`.`order_seq`;", NormalizeSql(sb.ToString()));
    }

    [Fact]
    public void GenerateObtainNextSequenceValueOperation_MatchesGenerateNextSequenceValueOperation()
    {
        const string name = "order_seq";
        const string schema = "myScope";

        var obtain = _generator.GenerateObtainNextSequenceValueOperation(name, schema);
        var next = _generator.GenerateNextSequenceValueOperation(name, schema);

        Assert.Equal(next, obtain);
    }

    [Fact]
    public void AppendObtainNextSequenceValueOperation_MatchesAppendNextSequenceValueOperation()
    {
        const string name = "order_seq";
        const string schema = "myScope";

        var sbObtain = new StringBuilder();
        _generator.AppendObtainNextSequenceValueOperation(sbObtain, name, schema);

        var sbNext = new StringBuilder();
        _generator.AppendNextSequenceValueOperation(sbNext, name, schema);

        Assert.Equal(sbNext.ToString(), sbObtain.ToString());
    }

    [Fact]
    public void AppendNextSequenceValueOperation_SequenceNameWithSpecialChars_EscapesIdentifier()
    {
        var sb = new StringBuilder();
        _generator.AppendNextSequenceValueOperation(sb, "my`seq", schema: null);
        // Backtick inside the name must be doubled per CouchbaseSqlGenerationHelper
        Assert.Contains("`my``seq`", sb.ToString());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string NormalizeSql(string sql)
        => sql.ReplaceLineEndings("\n").Trim();

    private static IReadOnlyModificationCommand BuildCommand(
        string tableName,
        string? schema,
        params IColumnModification[] columns)
    {
        var command = new Mock<IReadOnlyModificationCommand>();
        command.Setup(c => c.TableName).Returns(tableName);
        command.Setup(c => c.Schema).Returns(schema);
        command.Setup(c => c.ColumnModifications).Returns(columns.ToList());
        return command.Object;
    }

    // Column included in INSERT/UPDATE VALUES and SET clauses.
    // parameterName is the raw name WITHOUT the "$" prefix — the helper adds it.
    private static IColumnModification WriteCol(string name, string parameterName)
    {
        var col = new Mock<IColumnModification>();
        col.Setup(c => c.ColumnName).Returns(name);
        col.Setup(c => c.IsWrite).Returns(true);
        col.Setup(c => c.UseCurrentValueParameter).Returns(true);
        col.Setup(c => c.ParameterName).Returns(parameterName);
        return col.Object;
    }

    // Column included in the RETURNING clause.
    private static IColumnModification ReadCol(string name)
    {
        var col = new Mock<IColumnModification>();
        col.Setup(c => c.ColumnName).Returns(name);
        col.Setup(c => c.IsRead).Returns(true);
        return col.Object;
    }

    // Column included in the WHERE clause (key / condition).
    //
    // EF Core 10 AppendWhereCondition checks:
    //   Value == null  → IS NULL
    //   UseParameter   → = GenerateParameterNamePlaceholder(ParameterName)   ← we want this
    //   else           → = AppendSqlLiteral(...)  ← throws without TypeMapping
    private static IColumnModification ConditionCol(string name, string parameterName)
    {
        var col = new Mock<IColumnModification>();
        col.Setup(c => c.ColumnName).Returns(name);
        col.Setup(c => c.IsCondition).Returns(true);
        col.Setup(c => c.UseOriginalValueParameter).Returns(false);
        col.Setup(c => c.UseParameter).Returns(true);
        col.Setup(c => c.ParameterName).Returns(parameterName);
        col.Setup(c => c.Value).Returns(42);
        return col.Object;
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
