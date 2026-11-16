// Only compiled when the mod is built against .NET 8 reference assemblies (personal build without the
// .NET 6 targeting pack). The game runs MelonLoader on .NET 6, which does not ship these attribute types,
// so they are defined locally instead of referenced from System.Runtime.
#if NET8_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
    internal sealed class RefSafetyRulesAttribute : Attribute
    {
        public RefSafetyRulesAttribute(int version) => Version = version;
        public readonly int Version;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Interface |
                    AttributeTargets.Method | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte flag) => Flag = flag;
        public readonly byte Flag;
    }
}
#endif
