using System;

namespace UnityMcpBridge
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class UnityMcpCallableAttribute : Attribute
    {
        public bool AllowInEditMode { get; set; }
    }
}
