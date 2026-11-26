namespace Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// How the provider turns an entity's primary key into the Couchbase document key it saves,
/// replaces and deletes the document under.
/// </summary>
public enum CouchbaseDocumentKeyFormat
{
    /// <summary>
    /// The original format, used by 1.x. Choose it with <c>UseDocumentKeyFormat(Legacy)</c> to keep
    /// the keys of documents saved by 1.x. Key parts are joined with <c>_</c> in the reverse of the
    /// CLR declaration order of the key properties, formatted with the current culture, and not
    /// escaped. Because of that, reordering properties in the class changes the key, the same values
    /// saved under different cultures get different keys, and different composite keys can collide
    /// (<c>("x_y","z")</c> and <c>("y","z_x")</c> both give <c>z_x_y</c>). Every key part must be a
    /// public property of the entity class, or the save throws; a many-to-many join entity is the only
    /// exception, and its parts follow the declared key order.
    /// </summary>
    Legacy = 0,

    /// <summary>
    /// The default. Key parts follow the key's declared order (<c>HasKey(e =&gt; new { e.A, e.B })</c>
    /// gives <c>A</c> then <c>B</c>) and are formatted with the invariant culture. In a composite key,
    /// the delimiter and the escape character (<c>\</c>) are escaped in each part, so distinct keys
    /// always give distinct document keys; a single-part key is not escaped. A <c>byte[]</c> part is
    /// written as Base64 (an empty array as <c>~</c>), a <see cref="DateTime"/> or
    /// <see cref="DateTimeOffset"/> part in round-trip (<c>O</c>) format, and an enum as its
    /// underlying number. A <see langword="null"/> key part throws.
    /// </summary>
    /// <remarks>
    /// For a single key of type <see cref="int"/>, <see cref="long"/>, <see cref="Guid"/> or
    /// <see cref="string"/>, V2 and <see cref="Legacy"/> give the same document key. They differ for
    /// a composite key, and for a single key that is an enum, a date or time, a <see cref="decimal"/>,
    /// a floating-point number, or a value-converted value. Changing the format of a collection that
    /// has such documents changes their keys, and there is no automatic migration.
    /// </remarks>
    V2 = 1,
}
