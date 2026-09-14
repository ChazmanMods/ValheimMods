using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace RunicInventory.Tests
{
    internal static class DedicatedInstalledTests
    {
        internal static void Register()
        {
            TestRunner.Run("installed dedicated Valheim binary is the separately audited server build", ServerHashIsExact);
            TestRunner.Run("installed dedicated build retains inert authority and inventory contracts", ServerContractsAreExact);
        }

        private static void ServerHashIsExact()
        {
            using SHA256 sha = SHA256.Create();
            string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(TestPaths.InstalledDedicatedValheim)));
            TestAssert.Equal("F4EC6D8FC07054058F5E98040B3C1C65BDF0B061FD2ED27087EF48F29586B737", hash);
        }

        private static void ServerContractsAreExact()
        {
            using var stream = File.OpenRead(TestPaths.InstalledDedicatedValheim);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var provider = new SignatureNames();

            TypeDefinition version = Type(reader, "Version");
            TestAssert.True(version.GetProperties().Any(handle =>
                reader.GetString(reader.GetPropertyDefinition(handle).Name) == "CurrentVersion"));
            Method(reader, provider, version, "GetVersionString", "System.String", "System.Boolean");

            TypeDefinition inventory = Type(reader, "Inventory");
            Method(reader, provider, inventory, "GetWidth", "System.Int32");
            Method(reader, provider, inventory, "GetHeight", "System.Int32");
            Method(reader, provider, inventory, "GetAllItems", "System.Collections.Generic.List`1<ItemData>");
            Method(reader, provider, inventory, "Save", "System.Void", "ZPackage");
            Method(reader, provider, inventory, "FindEmptySlot", "Vector2i", "System.Boolean");

            Method(reader, provider, Type(reader, "Character"), "IsOwner", "System.Boolean");
            Method(reader, provider, Type(reader, "ZNet"), "IsServer", "System.Boolean");
        }

        private static TypeDefinition Type(MetadataReader reader, string name)
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                if (reader.GetString(type.Name) == name) return type;
            }
            throw new InvalidOperationException("Dedicated type " + name + " is missing.");
        }

        private static void Method(
            MetadataReader reader,
            SignatureNames provider,
            TypeDefinition type,
            string name,
            string returnType,
            params string[] parameters)
        {
            var observed = new System.Collections.Generic.List<string>();
            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if (reader.GetString(method.Name) != name) continue;
                MethodSignature<string> signature = method.DecodeSignature(provider, genericContext: null);
                observed.Add(signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")");
                if (signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters)) return;
            }
            throw new InvalidOperationException(
                "Dedicated " + reader.GetString(type.Name) + "." + name + " exact signature is missing; observed " +
                string.Join(";", observed) + ".");
        }

        private sealed class SignatureNames : ISignatureTypeProvider<string, object>
        {
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,*]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
                genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Void => "System.Void",
                _ => "System." + typeCode
            };
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                TypeDefinition definition = reader.GetTypeDefinition(handle);
                return FullName(reader, definition.Namespace, definition.Name);
            }
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                TypeReference reference = reader.GetTypeReference(handle);
                return FullName(reader, reference.Namespace, reference.Name);
            }
            public string GetTypeFromSpecification(
                MetadataReader reader,
                object genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind) =>
                reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

            private static string FullName(MetadataReader reader, StringHandle ns, StringHandle name)
            {
                string prefix = reader.GetString(ns);
                string local = reader.GetString(name);
                return prefix.Length == 0 ? local : prefix + "." + local;
            }
        }
    }
}
