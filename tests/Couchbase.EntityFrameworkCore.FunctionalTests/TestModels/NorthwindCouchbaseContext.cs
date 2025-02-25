// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestModels;

public class NorthwindCouchbaseContext: NorthwindRelationalContext
{
    public NorthwindCouchbaseContext(DbContextOptions options)
        : base(options)
    {
    }
}
