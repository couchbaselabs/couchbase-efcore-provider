using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMapping : RelationalTypeMapping
{
    private readonly Type _clrType;
    private readonly ValueComparer? _comparer;
    private readonly ValueComparer? _keyComparer;
    private readonly JsonValueReaderWriter? _jsonValueReaderWriter;

    protected CouchbaseTypeMapping(RelationalTypeMappingParameters relationalParameters) : base(relationalParameters)
    {
    }
    
   public CouchbaseTypeMapping(
        Type clrType,
        ValueComparer? comparer = null,
        ValueComparer? keyComparer = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null) 
       : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(clrType), "couchbase"))
   {
       _clrType = clrType;
       _comparer = comparer;
       _keyComparer = keyComparer;
       _jsonValueReaderWriter = jsonValueReaderWriter;
   }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new CouchbaseTypeMapping(parameters);
    }

    public override CoreTypeMapping WithComposedConverter(ValueConverter? converter, ValueComparer? comparer = null,
        ValueComparer? keyComparer = null, CoreTypeMapping? elementMapping = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
    {
        return new CouchbaseTypeMapping(Parameters.WithComposedConverter(converter, comparer, keyComparer,
            elementMapping, jsonValueReaderWriter));
    }
}