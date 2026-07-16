using System.Runtime.CompilerServices;

// Guarded by !SIGNED (a constant the .csproj defines only when SignRelease=true, the opt-in
// property that in turn enables SignAssembly — i.e. only in the release workflow's signing/pack
// step), not by #if DEBUG. Setting SignAssembly=true directly, without SignRelease=true, will NOT
// define SIGNED, so InternalsVisibleTo would stay present alongside a signed assembly (CS1726) —
// always go through SignRelease. The original #if DEBUG guard
// conflated "Release configuration" with "actually being strong-name signed" — they're
// orthogonal, and that conflation broke Release-configuration test builds entirely (a real,
// previously-undetected bug this CI setup caught: CS0122 "inaccessible due to its protection
// level" building the test projects in Release). A signed assembly requires each
// InternalsVisibleTo to specify a public key (CS1726), and the test assemblies are never signed,
// so these attributes can't be present at all once this assembly is actually signed. Guarding
// them out only for a signing build costs nothing: the release workflow packs this project
// standalone and never builds/runs the test projects against that signed output.
#if !SIGNED
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.UnitTests")]
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.IntegrationTests")]
#endif