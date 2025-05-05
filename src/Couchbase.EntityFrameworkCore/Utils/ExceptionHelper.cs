// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Couchbase.Core.Exceptions;

namespace Couchbase.EntityFrameworkCore.Utils;

public static class ExceptionHelper
{
    public static Exception NotSupportedException(string message)
    {
        throw new NotSupportedException(message);
    }

    public static Exception SyncroIONotSupportedException()
    {
        return NotSupportedException(
            "Couchbase EF Core Database Provider does not support synchronous I/O. " +
            "Make sure to use and correctly await only async methods when using Entity Framework " +
            "Core to access Couchbase database. See Couchbase EF Core Database Provider " +
            "documentation for more information.");
    }

    public static Exception InvalidKeyspaceFormatOrMissingCollection(string? keyspace, Exception? innerException = null)
    {
        return new CollectionNotFoundException($"The keyspace {keyspace} format is invalid. The keyspace " +
            "should be in the following format: [Bucket].[Scope].[Collection]. This usually indicates an issue " +
            "with modeling your entities to the Couchbase schema. Please investigate your Entity Modeling (OnModelCreating) so that " +
            "the correct keyspace is generate and that the same keyspace exists in Couchbase Server.", innerException);
    }
}
