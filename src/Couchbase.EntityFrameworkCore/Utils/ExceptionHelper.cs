// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
            "documentation  for more information.");
    }
}
