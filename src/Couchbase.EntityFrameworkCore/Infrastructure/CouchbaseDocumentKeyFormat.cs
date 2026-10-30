namespace Couchbase.EntityFrameworkCore.Infrastructure;

/// <summary>
/// How the provider turns an entity's primary key into the Couchbase document key it saves,
/// replaces and deletes the document under.
/// </summary>
public enum CouchbaseDocumentKeyFormat
{
    /// <summary>
    /// The original format, kept as the default so existing data stays addressable. Key parts are
    /// joined with <c>_</c> in the reverse of the CLR declaration order of the key properties,
    /// formatted with the current culture, and not escaped. Because of that, reordering properties
    /// in the class changes the key, the same values saved under different cultures get different
    /// keys, and different composite keys can collide (<c>("x_y","z")</c> and <c>("y","z_x")</c>
    /// both give <c>z_x_y</c>). Prefer <see cref="V2"/> for new collections.
    /// </summary>
    Legacy = 0,

    /// <summary>
    /// Key parts follow the key's declared order (<c>HasKey(e =&gt; new { e.A, e.B })</c> gives
    /// <c>A</c> then <c>B</c>), are formatted with the invariant culture, and have the delimiter and
    /// the escape character (<c>\</c>) escaped, so distinct keys always give distinct document keys.
    /// A <c>byte[]</c> part is written as Base64 (an empty array as <c>~</c>), a <see cref="DateTime"/>
    /// or <see cref="DateTimeOffset"/> part in round-trip (<c>O</c>) format, and an enum as its
    /// underlying number. A <see langword="null"/> key part throws.
    /// </summary>
    /// <remarks>
    /// Switching an existing collection from <see cref="Legacy"/> to V2 changes the document key of
    /// every composite-key document, and there is no automatic migration.
    /// </remarks>
    V2 = 1,
}
