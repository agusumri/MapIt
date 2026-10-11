using System.Threading;
using System.Threading.Tasks;

namespace MapIt
{
    /// <summary>
    /// Resolves a destination member value asynchronously.
    /// Used with <c>opt.MapFromAsync(...)</c> and resolved by <c>mapper.MapAsync(...)</c>.
    /// </summary>
    public interface IAsyncValueResolver<in TSource, in TDestination, TDestMember>
    {
        /// <summary>Resolves the destination member value asynchronously.</summary>
        Task<TDestMember> ResolveAsync(TSource source, TDestination destination, TDestMember destMember, ResolutionContext context, CancellationToken cancellationToken);
    }
}
