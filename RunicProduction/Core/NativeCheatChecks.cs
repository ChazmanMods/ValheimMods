using System;
using System.Reflection;

namespace RunicProduction.Core
{
    internal static class NativeCheatChecks
    {
        // 1.0.7 exposes a field; 1.0.12 exposes a computed property. Read the
        // native value each time, without caching or overriding its policy.
        internal static Func<bool> CreateReader(Type profileType)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            const string name = "s_bypassCheatChecks";
            PropertyInfo property = profileType?.GetProperty(name, flags);
            if (property != null && property.PropertyType == typeof(bool) &&
                property.GetIndexParameters().Length == 0 && property.GetGetMethod() != null)
                return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), property.GetGetMethod());
            FieldInfo field = profileType?.GetField(name, flags);
            if (field != null && field.FieldType == typeof(bool))
                return () => (bool)field.GetValue(null);
            throw new MissingMemberException(profileType?.FullName ?? "PlayerProfile", name);
        }
    }
}
