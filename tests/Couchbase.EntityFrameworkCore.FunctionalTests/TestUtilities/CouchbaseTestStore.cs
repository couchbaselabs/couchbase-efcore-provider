using System.Linq.Expressions;
using System.Reflection;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStore : TestStore
{
    private readonly TestStoreContext _storeContext;
    private readonly string _dataFilePath;
    private readonly Action<CouchbaseDbContextOptionsBuilder> _configureCosmos;
    private bool _initialized;
    
    public static CouchbaseTestStore GetOrCreate(string name, string dataFilePath)
        => new(name, dataFilePath: dataFilePath);
    
    public static CouchbaseTestStore Create(string name, Action<CouchbaseDbContextOptionsBuilder> extensionConfiguration = null)
        => new(name, shared: false, extensionConfiguration: extensionConfiguration);

    public static CouchbaseTestStore CreateInitialized(string name, Action<CouchbaseDbContextOptionsBuilder> extensionConfiguration = null)
        => (CouchbaseTestStore)Create(name, extensionConfiguration).Initialize(null, (Func<DbContext>)null);

    public static CouchbaseTestStore GetOrCreate(string name)
        => new(name);
    
    private CouchbaseTestStore(
        string name,
        bool shared = true,
        string dataFilePath = null,
        Action<CouchbaseDbContextOptionsBuilder> extensionConfiguration = null)
        : base(name, shared)
    {
        ConnectionString = TestEnvironment.ConnectionString;
        Username = TestEnvironment.Username;
        Password = TestEnvironment.Password;
        BucketName = TestEnvironment.BucketName;
        ScopeName = TestEnvironment.Scope;
        
        //CB doesn't have an ExecutionStrategy yet
        /*_configureCosmos = extensionConfiguration == null
            ? b => b.ApplyConfiguration()
            : b =>
            {
                b.ApplyConfiguration();
                extensionConfiguration(b);
            };*/

        _storeContext = new TestStoreContext(this);

        if (dataFilePath != null)
        {
            _dataFilePath = Path.Combine(
                Path.GetDirectoryName(typeof(CouchbaseTestStore).Assembly.Location),
                dataFilePath);
        }
    }
    
    public CouchbaseTestStore(string name, bool shared) : base(name, shared)
    {
    }
    
    public string ConnectionString { get; }
    
    public string Username { get; }
    
    public string Password { get; }
    
    public string BucketName { get; }
    
    public string ScopeName { get; }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
    {
        return builder.UseCouchbase<INamedBucketProvider>(new ClusterOptions()
            .WithConnectionString(ConnectionString)
            .WithCredentials(Username, Password), optionsBuilder =>
        {
            optionsBuilder.Bucket = BucketName;
            optionsBuilder.Scope = ScopeName;
        });
    }

    public override void Clean(DbContext context)
    {
        CleanAsync(context).GetAwaiter().GetResult();
    }

    public override async Task CleanAsync(DbContext context)
    {
        var cluster = context.Database.GetCouchbaseClient();
        await cluster.Buckets.FlushBucketAsync(BucketName);
    }

    private async Task CreateFromFile(DbContext context)
    {
        if (await EnsureCreatedAsync(context))
        {
            await context.Database.EnsureCreatedAsync();
            var cluster = context.Database.GetCouchbaseClient();
            var bucket = cluster.BucketAsync(BucketName).GetAwaiter().GetResult();
            var serializer = new JsonSerializer();
            
            using var fs = new FileStream(_dataFilePath, FileMode.Open, FileAccess.Read);
            using var sr = new StreamReader(fs);
            using var reader = new JsonTextReader(sr);
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.StartArray)
                {
                    NextEntityType:
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.StartObject)
                        {
                            string entityName = null;
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonToken.PropertyName)
                                {
                                    switch (reader.Value)
                                    {
                                        case "Name":
                                            reader.Read();
                                            entityName = (string)reader.Value;
                                            break;
                                        case "Data":
                                            while (reader.Read())
                                            {
                                                if (reader.TokenType == JsonToken.StartObject)
                                                {
                                                    var document = serializer.Deserialize<JObject>(reader);

                                                    document["id"] = $"{entityName}|{document["id"]}";
                                                    document["Discriminator"] = entityName;
                                                    
                                                    bucket
                                                        .Scope(ScopeName)
                                                        .Collection(entityName)
                                                        .UpsertAsync(document["id"].Value<string>(),document.ToString())
                                                        .GetAwaiter()
                                                        .GetResult();

                                                    /*await cosmosClient.CreateItemAsync(
                                                        "NorthwindContext", document, new FakeUpdateEntry());*/
                                                    
                                                }
                                                else if (reader.TokenType == JsonToken.EndObject)
                                                {
                                                    goto NextEntityType;
                                                }
                                            }

                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public async Task<bool> EnsureCreatedAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var cluster = context.Database.GetCouchbaseClient();
        var manager = cluster.Buckets;
        var found = false;
        try
        {
            await manager.GetBucketAsync(BucketName);
        }
        catch
        {
            found = false;
        }

        if (!found)
        {
            var settings = new BucketSettings
            {
                Name = BucketName,
                BucketType = BucketType.Couchbase
            };
            await manager.CreateBucketAsync(settings);
            found = true;
        }

        return found;
    }

    private class TestStoreContext : DbContext
    {
        private readonly CouchbaseTestStore _testStore;

        public TestStoreContext(CouchbaseTestStore testStore)
        {
            _testStore = testStore;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseCouchbase<INamedBucketProvider>(new ClusterOptions().
                WithConnectionString(TestEnvironment.ConnectionString).
                WithCredentials(TestEnvironment.Username, TestEnvironment.Password), optionsBuilder =>
            {
                optionsBuilder.Bucket = _testStore.BucketName;
                optionsBuilder.Scope = _testStore.ScopeName;
            });
        }
    }
    
    private class FakeUpdateEntry : IUpdateEntry
    {
        public IEntityType EntityType
            => new FakeEntityType();

        public EntityState EntityState { get => EntityState.Added; set => throw new NotImplementedException(); }

        public IUpdateEntry SharedIdentityEntry
            => throw new NotImplementedException();

        public object GetCurrentValue(IPropertyBase propertyBase)
            => throw new NotImplementedException();

        public TProperty GetCurrentValue<TProperty>(IPropertyBase propertyBase)
            => throw new NotImplementedException();

        public object GetOriginalValue(IPropertyBase propertyBase)
            => throw new NotImplementedException();

        public TProperty GetOriginalValue<TProperty>(IProperty property)
            => throw new NotImplementedException();

        public bool HasTemporaryValue(IProperty property)
            => throw new NotImplementedException();

        public bool IsModified(IProperty property)
            => throw new NotImplementedException();

        public bool IsStoreGenerated(IProperty property)
            => throw new NotImplementedException();

        public DbContext Context
            => throw new NotImplementedException();

        public void SetOriginalValue(IProperty property, object value)
            => throw new NotImplementedException();

        public void SetPropertyModified(IProperty property)
            => throw new NotImplementedException();

        public void SetStoreGeneratedValue(IProperty property, object value, bool setModified = true)
            => throw new NotImplementedException();

        public EntityEntry ToEntityEntry()
            => throw new NotImplementedException();

        public object GetRelationshipSnapshotValue(IPropertyBase propertyBase)
            => throw new NotImplementedException();

        public object GetPreStoreGeneratedCurrentValue(IPropertyBase propertyBase)
            => throw new NotImplementedException();

        public bool IsConceptualNull(IProperty property)
            => throw new NotImplementedException();
    }

    public class FakeEntityType : Annotatable, IEntityType
    {
        public IEntityType BaseType
            => throw new NotImplementedException();

        public string DefiningNavigationName
            => throw new NotImplementedException();

        public IEntityType DefiningEntityType
            => throw new NotImplementedException();

        public IModel Model
            => throw new NotImplementedException();

        public string Name
            => throw new NotImplementedException();

        public Type ClrType
            => throw new NotImplementedException();

        public bool HasSharedClrType
            => throw new NotImplementedException();

        public bool IsPropertyBag
            => throw new NotImplementedException();

        public InstantiationBinding ConstructorBinding
            => throw new NotImplementedException();

        public InstantiationBinding ServiceOnlyConstructorBinding
            => throw new NotImplementedException();

        IReadOnlyEntityType IReadOnlyEntityType.BaseType
            => throw new NotImplementedException();

        IReadOnlyModel IReadOnlyTypeBase.Model
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> FindDeclaredForeignKeys(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        public INavigation FindDeclaredNavigation(string name)
            => throw new NotImplementedException();

        public IProperty FindDeclaredProperty(string name)
            => throw new NotImplementedException();

        public IForeignKey FindForeignKey(IReadOnlyList<IProperty> properties, IKey principalKey, IEntityType principalEntityType)
            => throw new NotImplementedException();

        public IForeignKey FindForeignKey(
            IReadOnlyList<IReadOnlyProperty> properties,
            IReadOnlyKey principalKey,
            IReadOnlyEntityType principalEntityType)
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> FindForeignKeys(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        public IIndex FindIndex(IReadOnlyList<IProperty> properties)
            => throw new NotImplementedException();

        public IIndex FindIndex(string name)
            => throw new NotImplementedException();

        public IIndex FindIndex(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        public PropertyInfo FindIndexerPropertyInfo()
            => throw new NotImplementedException();

        public IKey FindKey(IReadOnlyList<IProperty> properties)
            => throw new NotImplementedException();

        public IKey FindKey(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        public IKey FindPrimaryKey()
            => throw new NotImplementedException();

        public IReadOnlyList<IReadOnlyProperty> FindProperties(IReadOnlyList<string> propertyNames)
            => throw new NotImplementedException();

        public IProperty FindProperty(string name)
            => null;

        public IServiceProperty FindServiceProperty(string name)
            => throw new NotImplementedException();

        public ISkipNavigation FindSkipNavigation(string name)
            => throw new NotImplementedException();

        public ChangeTrackingStrategy GetChangeTrackingStrategy()
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> GetDeclaredForeignKeys()
            => throw new NotImplementedException();

        public IEnumerable<IIndex> GetDeclaredIndexes()
            => throw new NotImplementedException();

        public IEnumerable<IKey> GetDeclaredKeys()
            => throw new NotImplementedException();

        public IEnumerable<INavigation> GetDeclaredNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetDeclaredProperties()
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> GetDeclaredReferencingForeignKeys()
            => throw new NotImplementedException();

        public IEnumerable<IServiceProperty> GetDeclaredServiceProperties()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlySkipNavigation> GetDeclaredSkipNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> GetDerivedForeignKeys()
            => throw new NotImplementedException();

        public IEnumerable<IIndex> GetDerivedIndexes()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlyNavigation> GetDerivedNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlyProperty> GetDerivedProperties()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlyServiceProperty> GetDerivedServiceProperties()
            => throw new NotImplementedException();

        public bool HasServiceProperties()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlySkipNavigation> GetDerivedSkipNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlyEntityType> GetDerivedTypes()
            => throw new NotImplementedException();

        public IEnumerable<IEntityType> GetDirectlyDerivedTypes()
            => throw new NotImplementedException();

        public string GetDiscriminatorPropertyName()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetForeignKeyProperties()
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> GetForeignKeys()
            => throw new NotImplementedException();

        public IEnumerable<IIndex> GetIndexes()
            => throw new NotImplementedException();

        public IEnumerable<IKey> GetKeys()
            => throw new NotImplementedException();

        public PropertyAccessMode GetNavigationAccessMode()
            => throw new NotImplementedException();

        public IEnumerable<INavigation> GetNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetProperties()
            => throw new NotImplementedException();

        public PropertyAccessMode GetPropertyAccessMode()
            => throw new NotImplementedException();

        public LambdaExpression GetQueryFilter()
            => throw new NotImplementedException();

        public IEnumerable<IForeignKey> GetReferencingForeignKeys()
            => throw new NotImplementedException();

        public IEnumerable<IDictionary<string, object>> GetSeedData(bool providerValues = false)
            => throw new NotImplementedException();

        public IEnumerable<IServiceProperty> GetServiceProperties()
            => throw new NotImplementedException();

        public Func<MaterializationContext, object> GetOrCreateMaterializer(IEntityMaterializerSource source)
            => throw new NotImplementedException();

        public Func<MaterializationContext, object> GetOrCreateEmptyMaterializer(IEntityMaterializerSource source)
            => throw new NotImplementedException();

        public IEnumerable<ISkipNavigation> GetSkipNavigations()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetValueGeneratingProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.FindDeclaredForeignKeys(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        IReadOnlyNavigation IReadOnlyEntityType.FindDeclaredNavigation(string name)
            => throw new NotImplementedException();

        IReadOnlyProperty IReadOnlyTypeBase.FindDeclaredProperty(string name)
            => throw new NotImplementedException();

        IReadOnlyForeignKey IReadOnlyEntityType.FindForeignKey(
            IReadOnlyList<IReadOnlyProperty> properties,
            IReadOnlyKey principalKey,
            IReadOnlyEntityType principalEntityType)
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.FindForeignKeys(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        IReadOnlyIndex IReadOnlyEntityType.FindIndex(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        IReadOnlyIndex IReadOnlyEntityType.FindIndex(string name)
            => throw new NotImplementedException();

        IReadOnlyKey IReadOnlyEntityType.FindKey(IReadOnlyList<IReadOnlyProperty> properties)
            => throw new NotImplementedException();

        IReadOnlyKey IReadOnlyEntityType.FindPrimaryKey()
            => throw new NotImplementedException();

        IReadOnlyProperty IReadOnlyTypeBase.FindProperty(string name)
            => throw new NotImplementedException();

        IReadOnlyServiceProperty IReadOnlyEntityType.FindServiceProperty(string name)
            => throw new NotImplementedException();

        IReadOnlySkipNavigation IReadOnlyEntityType.FindSkipNavigation(string name)
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.GetDeclaredForeignKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyIndex> IReadOnlyEntityType.GetDeclaredIndexes()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyKey> IReadOnlyEntityType.GetDeclaredKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyNavigation> IReadOnlyEntityType.GetDeclaredNavigations()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyProperty> IReadOnlyTypeBase.GetDeclaredProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.GetDeclaredReferencingForeignKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyServiceProperty> IReadOnlyEntityType.GetDeclaredServiceProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.GetDerivedForeignKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyIndex> IReadOnlyEntityType.GetDerivedIndexes()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyEntityType> IReadOnlyEntityType.GetDirectlyDerivedTypes()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.GetForeignKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyIndex> IReadOnlyEntityType.GetIndexes()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyKey> IReadOnlyEntityType.GetKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyNavigation> IReadOnlyEntityType.GetNavigations()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyProperty> IReadOnlyTypeBase.GetProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyForeignKey> IReadOnlyEntityType.GetReferencingForeignKeys()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyServiceProperty> IReadOnlyEntityType.GetServiceProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlySkipNavigation> IReadOnlyEntityType.GetSkipNavigations()
            => throw new NotImplementedException();

        IReadOnlyTrigger IReadOnlyEntityType.FindDeclaredTrigger(string name)
            => throw new NotImplementedException();

        ITrigger IEntityType.FindDeclaredTrigger(string name)
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyTrigger> IReadOnlyEntityType.GetDeclaredTriggers()
            => throw new NotImplementedException();

        IEnumerable<ITrigger> IEntityType.GetDeclaredTriggers()
            => throw new NotImplementedException();

        public IComplexProperty FindComplexProperty(string name)
            => throw new NotImplementedException();

        public IEnumerable<IComplexProperty> GetComplexProperties()
            => throw new NotImplementedException();

        public IEnumerable<IComplexProperty> GetDeclaredComplexProperties()
            => throw new NotImplementedException();

        IReadOnlyComplexProperty IReadOnlyTypeBase.FindComplexProperty(string name)
            => throw new NotImplementedException();

        public IReadOnlyComplexProperty FindDeclaredComplexProperty(string name)
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyComplexProperty> IReadOnlyTypeBase.GetComplexProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyComplexProperty> IReadOnlyTypeBase.GetDeclaredComplexProperties()
            => throw new NotImplementedException();

        public IEnumerable<IReadOnlyComplexProperty> GetDerivedComplexProperties()
            => throw new NotImplementedException();

        public IEnumerable<IPropertyBase> GetMembers()
            => throw new NotImplementedException();

        public IEnumerable<IPropertyBase> GetDeclaredMembers()
            => throw new NotImplementedException();

        public IPropertyBase FindMember(string name)
            => throw new NotImplementedException();

        public IEnumerable<IPropertyBase> FindMembersInHierarchy(string name)
            => throw new NotImplementedException();

        public IEnumerable<IPropertyBase> GetSnapshottableMembers()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetFlattenedProperties()
            => throw new NotImplementedException();

        public IEnumerable<IComplexProperty> GetFlattenedComplexProperties()
            => throw new NotImplementedException();

        public IEnumerable<IProperty> GetFlattenedDeclaredProperties()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyPropertyBase> IReadOnlyTypeBase.GetMembers()
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyPropertyBase> IReadOnlyTypeBase.GetDeclaredMembers()
            => throw new NotImplementedException();

        IReadOnlyPropertyBase IReadOnlyTypeBase.FindMember(string name)
            => throw new NotImplementedException();

        IEnumerable<IReadOnlyPropertyBase> IReadOnlyTypeBase.FindMembersInHierarchy(string name)
            => throw new NotImplementedException();
    }
}