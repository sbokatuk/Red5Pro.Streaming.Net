using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Red5Pro.Streaming.Net.PackageTests;

/// <summary>
/// Reads the public API out of a binding assembly using metadata only. The assemblies target
/// *-android and *-ios and reference Mono.Android / Microsoft.iOS, so they cannot be loaded into
/// the test process; the metadata reader lets these tests run on a plain desktop runner with no
/// emulator, simulator or workload installed — including the Linux one CI validates on.
/// </summary>
public sealed class AssemblyApi : IDisposable
{
    private readonly PEReader _peReader;
    private readonly MetadataReader _metadata;
    private IReadOnlyList<string>? _publicTypes;

    public AssemblyApi(Stream assembly)
    {
        _peReader = new PEReader(assembly);
        _metadata = _peReader.GetMetadataReader();
    }

    /// <summary>
    /// Namespace-qualified names of every publicly reachable type, nested ones included as
    /// <c>Outer+Inner</c>.
    ///
    /// Nested types carry NestedPublic rather than Public and an empty Namespace, so a naive
    /// visibility filter misses them entirely — which matters here because the Android SDK puts its
    /// listener interfaces inside IRed5WebrtcClient.
    /// </summary>
    public IReadOnlyList<string> PublicTypes => _publicTypes ??= _metadata.TypeDefinitions
        .Select(_metadata.GetTypeDefinition)
        .Where(IsPubliclyVisible)
        .Select(FullNameOf)
        .ToList();

    private bool IsPubliclyVisible(TypeDefinition type)
    {
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;

        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        // A nested type is only reachable if every type enclosing it is too.
        return visibility == TypeAttributes.NestedPublic
               && type.GetDeclaringType() is { IsNil: false } declaring
               && IsPubliclyVisible(_metadata.GetTypeDefinition(declaring));
    }

    public IReadOnlyList<string> MethodsOf(string typeFullName)
    {
        var type = FindType(typeFullName);
        return type.GetMethods()
            .Select(_metadata.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
            .Select(method => _metadata.GetString(method.Name))
            .ToList();
    }

    private TypeDefinition FindType(string typeFullName)
    {
        foreach (var handle in _metadata.TypeDefinitions)
        {
            var type = _metadata.GetTypeDefinition(handle);
            if (FullNameOf(type) == typeFullName)
            {
                return type;
            }
        }

        throw new InvalidOperationException($"Type '{typeFullName}' is not defined in this assembly.");
    }

    private string FullNameOf(TypeDefinition type)
    {
        var name = _metadata.GetString(type.Name);

        // Nested types have an empty Namespace; the namespace belongs to the outermost type, so it
        // has to be walked up to rather than read off this one.
        if (type.GetDeclaringType() is { IsNil: false } declaring)
        {
            return $"{FullNameOf(_metadata.GetTypeDefinition(declaring))}+{name}";
        }

        var ns = _metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public void Dispose() => _peReader.Dispose();
}
