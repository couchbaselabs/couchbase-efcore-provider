using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.Update.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseModificationCommandBatchFactoryTests
{
    private readonly CouchbaseModificationCommandBatchFactory _factory;

    public CouchbaseModificationCommandBatchFactoryTests()
    {
        var helper = new CouchbaseSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
        var typeMappingSource = new Mock<IRelationalTypeMappingSource>().Object;
        var updateSqlGenerator = new CouchbaseUpdateSqlGenerator(
            new UpdateSqlGeneratorDependencies(helper, typeMappingSource));

        var dependencies = new ModificationCommandBatchFactoryDependencies(
            new Mock<IRelationalCommandBuilderFactory>().Object,
            helper,
            updateSqlGenerator,
            new Mock<ICurrentDbContext>().Object,
            new Mock<IRelationalCommandDiagnosticsLogger>().Object,
            new Mock<IDiagnosticsLogger<DbLoggerCategory.Update>>().Object);

        _factory = new CouchbaseModificationCommandBatchFactory(dependencies);
    }

    [Fact]
    public void Create_ReturnsNonNull()
    {
        var batch = _factory.Create();

        Assert.NotNull(batch);
    }

    [Fact]
    public void Create_ReturnsSingularModificationCommandBatch()
    {
        var batch = _factory.Create();

        Assert.IsType<SingularModificationCommandBatch>(batch);
    }

    [Fact]
    public void Create_ReturnsNewInstanceEachCall()
    {
        var batch1 = _factory.Create();
        var batch2 = _factory.Create();

        Assert.NotSame(batch1, batch2);
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
