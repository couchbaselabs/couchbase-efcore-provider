using Couchbase.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.ValueGeneration;

public class CouchbaseSequenceOptionsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        // Arrange & Act
        var options = CouchbaseSequenceOptions.Default;

        // Assert
        Assert.Equal(1, options.StartWith);
        Assert.Equal(1, options.IncrementBy);
        Assert.Null(options.MaxValue);
        Assert.Null(options.MinValue);
        Assert.False(options.Cycle);
        Assert.Null(options.CacheSize);
    }

    [Fact]
    public void Constructor_WithInitializers_SetsProperties()
    {
        // Arrange & Act
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = 5,
            MaxValue = 1000,
            MinValue = 50,
            Cycle = true,
            CacheSize = 20
        };

        // Assert
        Assert.Equal(100, options.StartWith);
        Assert.Equal(5, options.IncrementBy);
        Assert.Equal(1000, options.MaxValue);
        Assert.Equal(50, options.MinValue);
        Assert.True(options.Cycle);
        Assert.Equal(20, options.CacheSize);
    }

    [Fact]
    public void ToSqlOptionsClause_WithDefaults_ReturnsBasicClause()
    {
        // Arrange
        var options = CouchbaseSequenceOptions.Default;

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Equal("START WITH 1 INCREMENT BY 1 NO CYCLE", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithCustomStartAndIncrement_ReturnsCorrectClause()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 1000,
            IncrementBy = 10
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Equal("START WITH 1000 INCREMENT BY 10 NO CYCLE", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithMaxValue_IncludesMaxValue()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 1,
            IncrementBy = 1,
            MaxValue = 999999
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("MAXVALUE 999999", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithMinValue_IncludesMinValue()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 100,
            IncrementBy = -1,
            MinValue = 1
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("MINVALUE 1", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithCycle_IncludesCycle()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            Cycle = true
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("CYCLE", clause);
        Assert.DoesNotContain("NO CYCLE", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithCacheSize_IncludesCache()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            CacheSize = 100
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("CACHE 100", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithAllOptions_ReturnsCompleteClause()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 500,
            IncrementBy = 5,
            MaxValue = 10000,
            MinValue = 100,
            Cycle = true,
            CacheSize = 50
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("START WITH 500", clause);
        Assert.Contains("INCREMENT BY 5", clause);
        Assert.Contains("MAXVALUE 10000", clause);
        Assert.Contains("MINVALUE 100", clause);
        Assert.Contains("CYCLE", clause);
        Assert.Contains("CACHE 50", clause);
        Assert.DoesNotContain("NO CYCLE", clause);
    }

    [Fact]
    public void ToSqlOptionsClause_WithNegativeIncrement_SupportsDescendingSequence()
    {
        // Arrange
        var options = new CouchbaseSequenceOptions
        {
            StartWith = 1000,
            IncrementBy = -1,
            MinValue = 1
        };

        // Act
        var clause = options.ToSqlOptionsClause();

        // Assert
        Assert.Contains("INCREMENT BY -1", clause);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var options1 = new CouchbaseSequenceOptions { StartWith = 100, IncrementBy = 5 };
        var options2 = new CouchbaseSequenceOptions { StartWith = 100, IncrementBy = 5 };

        // Act & Assert
        Assert.Equal(options1, options2);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var options1 = new CouchbaseSequenceOptions { StartWith = 100 };
        var options2 = new CouchbaseSequenceOptions { StartWith = 200 };

        // Act & Assert
        Assert.NotEqual(options1, options2);
    }
}
