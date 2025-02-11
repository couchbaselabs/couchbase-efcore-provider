// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public class CouchbaseNorthwindTestStoreFactory : CouchbaseTestStoreFactory
{
    public static new CouchbaseNorthwindTestStoreFactory Instance { get; } = new();

    protected CouchbaseNorthwindTestStoreFactory()
    {
    }

    public override TestStore GetOrCreate(string storeName)
        => CouchbaseTestStore.GetExisting("northwind");
}
