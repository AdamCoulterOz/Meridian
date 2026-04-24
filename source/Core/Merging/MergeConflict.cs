namespace Meridian.Core.Merging;

public sealed record MergeConflict(
    ConflictKind Kind,
    string Path,
    string? BaseText,
    string? OursText,
    string? TheirsText,
    string Message);

public enum ConflictKind
{
    Scalar,
    Node,
    OrderedChildren,
    AmbiguousIdentity
}
