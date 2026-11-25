using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Extensions;

internal static class NavigationExtensions
{
    /// <summary>
    /// True when <paramref name="navigation"/> goes from an owner to an owned type it owns.
    /// An owned type's navigation back to its owner (<c>WithOwner</c>) is false: the owner is
    /// not part of the owned item's JSON, and following it would recurse without end.
    /// </summary>
    internal static bool IsOwnedChild(this INavigation navigation)
        => navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent;
}
