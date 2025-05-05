// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

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
