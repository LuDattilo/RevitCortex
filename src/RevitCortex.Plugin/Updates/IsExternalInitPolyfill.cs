// Polyfill required for C# 9 'record' types on net48.
// The compiler synthesises init-only setters that depend on this type,
// which ships in-box on .NET 5+ but must be declared manually on net48.
#if NET48
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
