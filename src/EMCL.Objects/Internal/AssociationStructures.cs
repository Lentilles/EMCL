namespace EMCL.Objects.Internal;

internal static class AssociationStructures
{
    internal static class ObjectEntry
    {
        internal const int Count = 4;
        internal const int ClassId = 0;
        internal const int Version = 1;
        internal const int LogicalName = 2;
        internal const int AccessRights = 3;
    }
    internal static class AccessRights
    {
        internal const int Count = 2;
        internal const int Attributes = 0;
        internal const int Methods = 1;
    }
    internal static class AttributeAccess
    {
        internal const int Count = 3;
        internal const int Index = 0;
        internal const int Mode = 1;
        internal const int Selectors = 2;
    }
    internal static class MethodAccess
    {
        internal const int Count = 2;
        internal const int Index = 0;
        internal const int Mode = 1;
    }
    internal static class User
    {
        internal const int Count = 2;
        internal const int Id = 0;
        internal const int Name = 1;
    }
}
