using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Couchbase.Core.Utils;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.Extensions.DependencyInjection;
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
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseTestStore : RelationalTestStore
{
    public const int CommandTimeout = 30;
    private readonly string _dataFilePath;

    public static CouchbaseTestStore GetOrCreate(string name, bool sharedCache = false)
        => new(name, sharedCache: sharedCache);

    public static CouchbaseTestStore GetOrCreateInitialized(string name)
    {
        return new CouchbaseTestStore(name).InitializeCouchbase(
            new ServiceCollection().AddEntityFrameworkCouchbase(
                    new CouchbaseOptionsExtension(
                        new CouchbaseDbContextOptionsBuilder(
                            new DbContextOptionsBuilder(),
                            new ClusterOptions()
                                .WithLogging(LoggerFactory.Create(builder =>
                                        {
                                            builder.AddFilter(level => level >= LogLevel.Debug);
                                            builder.AddFile("Logs/myapp-{Date}-1.txt", LogLevel.Debug);
                                        }))
                                .WithConnectionString(TestEnvironment.ConnectionString)
                                .WithCredentials(TestEnvironment.Username, TestEnvironment.Password))))
                .BuildServiceProvider(validateScopes: true),
            (Func<DbContext>)null,
            null);
    }

    public static CouchbaseTestStore GetExisting(string name)
        => new(name);

    public static CouchbaseTestStore Create(string name)
        => new(name, shared: true);
       // => new(name, shared: false);

    private readonly bool _seed = false;

    private CouchbaseTestStore(string name, bool seed = false, bool sharedCache = false, bool shared = true, string dataFilePath = null)
        : base(name, shared)
    {
        _seed = seed;

        ConnectionString = TestEnvironment.ConnectionString;

        var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFilter(level => level >= LogLevel.Debug);
                builder.AddFile("Logs/myapp-{Date}-2.txt", LogLevel.Debug);
            });

        var dbConnectionOptions = new CouchbaseDbContextOptionsBuilder(
            new DbContextOptionsBuilder(),
            new ClusterOptions()
                .WithLogging(loggerFactory)
                .WithConnectionString(TestEnvironment.ConnectionString)
                .WithCredentials(TestEnvironment.Username, TestEnvironment.Password));

        var services = new ServiceCollection();
        var extension = new CouchbaseOptionsExtension(dbConnectionOptions);
        extension.ApplyServices(services);
        services.AddEntityFrameworkCouchbase(new CouchbaseOptionsExtension(dbConnectionOptions));

        ServiceProvider = services.BuildServiceProvider();
        var connection = new CouchbaseConnection(ServiceProvider.GetRequiredService<IBucketProvider>(), dbConnectionOptions);
        Connection = connection;

        var path = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase);
        path += "/Northwind.json";

        if (dataFilePath != null)
        {
            _dataFilePath = Path.Combine(
                Path.GetDirectoryName(typeof(CouchbaseTestStore).Assembly.Location),
                dataFilePath);
        }

        dataFilePath = path;
    }

    public virtual DbContextOptionsBuilder AddProviderOptions(
        DbContextOptionsBuilder builder,
        Action<CouchbaseDbContextOptionsBuilder> configureSqlite)
    {
        return builder.UseCouchbase(
            new ClusterOptions()
                .WithCredentials("Administrator", "password")
                .WithConnectionString("couchbase://localhost")
                .WithLogging(
                    LoggerFactory.Create(
                        builder =>
                            {
                                builder.AddFilter(level => level >= LogLevel.Debug);
                                builder.AddFile("Logs/myapp-{Date}-3.txt", LogLevel.Debug);
                            })),
            couchbaseDbContextOptions =>
                {
                    couchbaseDbContextOptions.Bucket = "Content";
                    couchbaseDbContextOptions.Scope = "Blogs";
                });
    }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => AddProviderOptions(builder, configureSqlite: null);

    public CouchbaseTestStore InitializeCouchbase(IServiceProvider serviceProvider, Func<DbContext> createContext, Action<DbContext> seed)
        => (CouchbaseTestStore)Initialize(serviceProvider, createContext, seed);

    public CouchbaseTestStore InitializeCouchbase(
        IServiceProvider serviceProvider,
        Func<CouchbaseTestStore, DbContext> createContext,
        Action<DbContext> seed)
        => (CouchbaseTestStore)Initialize(serviceProvider, () => createContext(this), seed);

    protected override void Initialize(Func<DbContext> createContext, Action<DbContext> seed, Action<DbContext> clean)
    {
        if (!_seed)
        {
            return;
        }

        using var context = createContext();
        if (!context.Database.EnsureCreated())
        {
            clean?.Invoke(context);
            CleanAsync(context).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        seed?.Invoke(context);
        CreateFromFile(context).GetAwaiter().GetResult();
    }

    public override async Task CleanAsync(DbContext context)
    {
        if (_seed)
        {
           await context.Database.EnsureCleanAsync().ConfigureAwait(false);
        }
    }

    public override void Clean(DbContext context)
    {
        if (_seed)
        {
            context.Database.EnsureClean();
        }
    }

    public int ExecuteNonQuery(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteNonQuery();
    }

    public T ExecuteScalar<T>(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return (T)command.ExecuteScalar();
    }

    private DbCommand CreateCommand(string commandText, object[] parameters)
    {
        var command = (CouchbaseCommand)Connection.CreateCommand();

        command.CommandText = commandText;
        command.CommandTimeout = CommandTimeout;

        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue("@p" + i, parameters[i]);
        }

        return command;
    }

    private async Task CreateFromFile(DbContext context)
    {
        if (_seed)
        {

           var path = "Northwind.json";

            var serializer = Newtonsoft.Json.JsonSerializer.Create();
            await context.Database.EnsureCreatedAsync();
            var couchbaseClient = context.GetService<ICouchbaseClientWrapper>();
            using var fs = new FileStream(/*_dataFilePath*/ path, FileMode.Open, FileAccess.Read);
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

                                                    var key =  $"{entityName}|{document["id"]}";
                                                    document["id"] = key;
                                                    document["Discriminator"] = entityName;

                                                    var keyspace =
                                                        $"{entityName}.{TestEnvironment.BucketName}.{TestEnvironment.Scope}";

                                                    var json = document.ToObject<object>();
                                                    await couchbaseClient.CreateDocument(key, keyspace, json);
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

    public override string NormalizeDelimitersInRawString(string sql)
        => sql.Replace("[", OpenDelimiter).Replace("]", CloseDelimiter);

    public override FormattableString NormalizeDelimitersInInterpolatedString(FormattableString sql)
        => new TestFormattableString(NormalizeDelimitersInRawString(sql.Format), sql.GetArguments());

    protected override string OpenDelimiter
        => "`";

    protected override string CloseDelimiter
        => "`";

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
