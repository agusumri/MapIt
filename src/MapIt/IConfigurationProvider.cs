using System;
using System.Linq.Expressions;

namespace MapIt
{
    /// <summary>
    /// Provides configuration and mapper creation.
    /// </summary>
    public interface IConfigurationProvider
    {
        /// <summary>Validates that all configured maps are resolvable.</summary>
        void AssertConfigurationIsValid();

        /// <summary>Creates a mapper from the current configuration.</summary>
        IMapper CreateMapper();

        /// <summary>Creates a mapper that uses the given service constructor.</summary>
        IMapper CreateMapper(Func<Type, object> serviceCtor);

        /// <summary>Builds the execution plan (expression) for a mapping.</summary>
        LambdaExpression BuildExecutionPlan(Type sourceType, Type destinationType);

        /// <summary>Precompiles all mapping plans.</summary>
        void CompileMappings();
    }
}
