using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Extensions;

public class CouchbaseServiceCollectionExtensionsTests
{
    private static IServiceCollection BuildRegisteredServices()
    {
        var services = new ServiceCollection();
        var optionsBuilder = new DbContextOptionsBuilder();
        var clusterOptions = new ClusterOptions().WithConnectionString("couchbase://localhost");
        var couchbaseOptionsBuilder = new CouchbaseDbContextOptionsBuilder(optionsBuilder, clusterOptions);
        var extension = new CouchbaseOptionsExtension(couchbaseOptionsBuilder);
        services.AddEntityFrameworkCouchbase(extension);
        return services;
    }

    [Fact]
    public void AddEntityFrameworkCouchbase_IQueryCompilationContextFactory_ResolvesToCouchbaseFactory()
    {
        var services = BuildRegisteredServices();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IQueryCompilationContextFactory));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(CouchbaseQueryCompilationContextFactory), descriptor.ImplementationType);
    }

    [Fact]
    public void AddEntityFrameworkCouchbase_IQueryCompilationContextFactory_RegisteredExactlyOnce()
    {
        var services = BuildRegisteredServices();

        var count = services.Count(d => d.ServiceType == typeof(IQueryCompilationContextFactory));

        Assert.Equal(1, count);
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
