using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Couchbase;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.EntityFrameworkCore.Utils;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseOptionsExtension: RelationalOptionsExtension
{
    private readonly CouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private CouchbaseOptionsExtensionInfo? _info;

    public CouchbaseOptionsExtension(CouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
    }
    protected internal CouchbaseOptionsExtension(CouchbaseOptionsExtension copyFrom)
        : base(copyFrom)
    {
        _couchbaseDbContextOptionsBuilder = copyFrom.CouchbaseDbContextOptionsBuilder;
    }

    public CouchbaseDbContextOptionsBuilder? CouchbaseDbContextOptionsBuilder => _couchbaseDbContextOptionsBuilder;

    public override string? ConnectionString => _couchbaseDbContextOptionsBuilder.ConnectionString;

    public override DbContextOptionsExtensionInfo Info => _info ??= new CouchbaseOptionsExtensionInfo(this);

    public CouchbaseDbContextOptionsBuilder DbContextOptionsBuilder => _couchbaseDbContextOptionsBuilder;

    public override void ApplyServices(IServiceCollection services)
    {
        services.AddCouchbase(options =>
        {
            options.WithLogging(_couchbaseDbContextOptionsBuilder.ClusterOptions.Logging);
            options.WithConnectionString(_couchbaseDbContextOptionsBuilder.ClusterOptions.ConnectionString);
            options.WithCredentials(_couchbaseDbContextOptionsBuilder.ClusterOptions.UserName, _couchbaseDbContextOptionsBuilder.ClusterOptions.Password);
        });

        services.AddEntityFrameworkCouchbase(this);
    }

    public override void Validate(IDbContextOptions options)
    {
        // You can add any validation logic here, if necessary.
    }

    protected override RelationalOptionsExtension Clone() => new CouchbaseOptionsExtension(this);


    public class CouchbaseOptionsExtensionInfo : DbContextOptionsExtensionInfo
    {
        public CouchbaseOptionsExtensionInfo(CouchbaseOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => true;

        public override string LogFragment => $"Using Custom Couchbase Provider - ConnectionString: {ConnectionString}";

        public override int GetServiceProviderHashCode() => ConnectionString.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is CouchbaseOptionsExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Couchbase:ConnectionString"] = ConnectionString;
        }

        public override CouchbaseOptionsExtension Extension => (CouchbaseOptionsExtension)base.Extension;
        private string? ConnectionString => Extension.Connection == null ?
            Extension.ConnectionString :
            Extension.Connection.ConnectionString;
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2021 Couchbase, Inc.
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
