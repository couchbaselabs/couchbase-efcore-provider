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

    public static Exception InvalidKeyspaceFormatOrMissingCollection(string? keyspace, Exception? innerException = null)
    {
        return new CollectionNotFoundException(
            $"The keyspace {keyspace} format is invalid. The keyspace should be in the format: " +
            "`Bucket`.`Scope`.`Collection` (e.g., `travel-sample`.`inventory`.`airline`). " +
            "This usually indicates an issue with modeling your entities to the Couchbase schema. " +
            "Please investigate your Entity Modeling (OnModelCreating) so that the correct keyspace " +
            "is generated and that the same keyspace exists in Couchbase Server.", innerException);
    }
}
