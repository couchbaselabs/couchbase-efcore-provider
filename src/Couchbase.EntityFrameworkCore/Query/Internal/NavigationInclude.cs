// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Records a single navigation include (from .Include() or .ThenInclude()) for use
/// during result shaping. Children represent ThenInclude chains.
/// <para>
/// <see cref="Navigation"/> may be an <see cref="INavigation"/> (FK-based) or an
/// <see cref="ISkipNavigation"/> (HasMany/WithMany transparent join table) — both
/// implement <see cref="INavigationBase"/>.
/// </para>
/// </summary>
public record NavigationInclude(
    INavigationBase Navigation,
    LambdaExpression? Filter,
    List<NavigationInclude> Children);
