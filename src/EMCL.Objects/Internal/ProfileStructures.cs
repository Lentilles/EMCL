namespace EMCL.Objects.Internal;

internal static class ProfileStructures
{
    internal static class CaptureObject
    {
        internal const int Count = 4;
        internal const int ClassId = 0;
        internal const int LogicalName = 1;
        internal const int AttributeIndex = 2;
        internal const int DataIndex = 3;
    }
    internal static class EntrySelection
    {
        internal const int Count = 4;
        internal const int FirstEntry = 0;
        internal const int LastEntry = 1;
        internal const int FirstColumn = 2;
        internal const int LastColumn = 3;
    }
    internal static class RangeSelection
    {
        internal const int Count = 4;
        internal const int RestrictingObject = 0;
        internal const int FromValue = 1;
        internal const int ToValue = 2;
        internal const int SelectedValues = 3;
    }
}
