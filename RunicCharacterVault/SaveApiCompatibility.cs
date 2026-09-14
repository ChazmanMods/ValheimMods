using System;
using System.Reflection;
using Splatform;

namespace RunicCharacterVault
{
    // Valheim changed these public save API signatures in the public test branch.
    // This narrow adapter preserves local and cloud saves without binding the DLL
    // to either signature. Do not expand it beyond save API compatibility.
    internal static class SaveApiCompatibility
    {
        private static readonly Type[] LegacyWriterParameters =
            { typeof(string), typeof(FileHelpers.FileHelperType), typeof(FileHelpers.FileSource) };
        private static readonly Type[] CurrentWriterParameters =
            { typeof(string), typeof(CloudStorageFileGrouping), typeof(FileHelpers.FileHelperType),
                typeof(FileHelpers.FileSource) };

        internal static FileHelpers.FileSource LocalSource =>
            (FileHelpers.FileSource)Enum.Parse(typeof(FileHelpers.FileSource), "Local");

        internal static FileWriter CreateWriter(string path, FileHelpers.FileSource source)
        {
            ConstructorInfo constructor = typeof(FileWriter).GetConstructor(CurrentWriterParameters);
            if (constructor != null)
            {
                return (FileWriter)constructor.Invoke(new object[] { path,
                    CloudStorageFileGrouping.Individual, FileHelpers.FileHelperType.Binary, source });
            }
            constructor = typeof(FileWriter).GetConstructor(LegacyWriterParameters) ??
                throw new MissingMethodException(typeof(FileWriter).FullName, ".ctor");
            return (FileWriter)constructor.Invoke(new object[]
                { path, FileHelpers.FileHelperType.Binary, source });
        }

        internal static void ReplaceOldFile(string path, string next, FileHelpers.FileSource source)
        {
            MethodInfo method = FindMethod(typeof(FileHelpers), "ReplaceOldFile",
                typeof(string), typeof(string), typeof(string),
                typeof(CloudStorageFileGrouping), typeof(FileHelpers.FileSource));
            object[] arguments = method != null
                ? new object[] { path, next, path + ".old", CloudStorageFileGrouping.Individual, source }
                : new object[] { path, next, path + ".old", source };
            (method ?? RequireMethod(typeof(FileHelpers), "ReplaceOldFile",
                typeof(string), typeof(string), typeof(string), typeof(FileHelpers.FileSource)))
                .Invoke(null, arguments);
        }

        internal static void InvalidateCharacterCache()
        {
            MethodInfo method = FindMethod(typeof(SaveSystem), "InvalidateCache", typeof(SaveDataType));
            if (method != null)
            {
                method.Invoke(null, new object[] { SaveDataType.Character });
                return;
            }
            RequireMethod(typeof(SaveSystem), "InvalidateCache").Invoke(null, null);
        }

        internal static string GetCharacterPath(FileHelpers.FileSource source, string filename)
        {
            MethodInfo method = FindMethod(typeof(SaveSystem), "GetCharacterPath",
                typeof(FileHelpers.FileSource), typeof(string));
            if (method != null)
            {
                return (string)method.Invoke(null, new object[] { source, filename });
            }
            return (string)RequireMethod(typeof(PlayerProfile), "GetPath",
                typeof(FileHelpers.FileSource), typeof(string))
                .Invoke(null, new object[] { source, filename });
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameters)
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                null, parameters, null);
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters)
        {
            return FindMethod(type, name, parameters) ??
                throw new MissingMethodException(type.FullName, name);
        }
    }
}
