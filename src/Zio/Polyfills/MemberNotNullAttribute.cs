namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    internal sealed class MemberNotNullAttribute : Attribute
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public MemberNotNullAttribute(params string[] members) { }
#pragma warning restore IDE0060 // Remove unused parameter
    }
}