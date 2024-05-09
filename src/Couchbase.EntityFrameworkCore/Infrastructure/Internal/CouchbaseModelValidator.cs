using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseModelValidator : ModelValidator
{
    public CouchbaseModelValidator(ModelValidatorDependencies dependencies)
        : base(dependencies)
    {
    }
    
    //TODO override required methods
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);
    }

    protected override void ValidatePropertyMapping(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.ValidatePropertyMapping(model, logger);
    }
}