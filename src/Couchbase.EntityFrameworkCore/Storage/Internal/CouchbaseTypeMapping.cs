using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseTypeMapping : CoreTypeMapping
{
    protected CouchbaseTypeMapping(CoreTypeMappingParameters parameters) : base(parameters)
    {
    }
    
    public CouchbaseTypeMapping(
        Type clrType,
        ValueComparer? comparer = null,
        ValueComparer? keyComparer = null,
        JsonValueReaderWriter? jsonValueReaderWriter = null)
        : base(
            new CoreTypeMappingParameters(
                clrType,
                converter: null,
                comparer,
                keyComparer,
                jsonValueReaderWriter: jsonValueReaderWriter))
    {
    }

    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters)
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