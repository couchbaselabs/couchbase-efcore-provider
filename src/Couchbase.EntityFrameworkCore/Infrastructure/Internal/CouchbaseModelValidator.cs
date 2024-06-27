using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseModelValidator : RelationalModelValidator
{
    public CouchbaseModelValidator(ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
        
    }
    
    //TODO override required methods
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);
        ValidatePropertyMapping(model, logger);
       
    }

    protected override void ValidatePropertyMapping(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        IConventionModel conventionModel = model as IConventionModel;
        if (conventionModel == null)
            return;
        
        foreach (IConventionTypeBase entityType in conventionModel.GetEntityTypes())
        {
            var unmappedProperty = entityType.GetDeclaredProperties().FirstOrDefault(
                p => (!ConfigurationSource.Convention.Overrides(p.GetConfigurationSource())
                      // Use a better condition for non-persisted properties when issue #14121 is implemented
                      || !p.IsImplicitlyCreated())
                     && p.FindTypeMapping() == null);

            if (unmappedProperty != null)
            {
            }
            
            
        } 
    }
}