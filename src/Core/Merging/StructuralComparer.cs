using Meridian.Core.Tree;

namespace Meridian.Core.Merging;

public static class StructuralComparer
{
    public static bool Equals(TreeNode? left, TreeNode? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        if (!string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) ||
                            !string.Equals(left.Value, right.Value, StringComparison.Ordinal) ||
                            left.Fields.Count != right.Fields.Count ||
                            left.Children.Count != right.Children.Count)
            return false;

        foreach (var field in left.Fields)
            if (!right.Fields.TryGetValue(field.Key, out var rightValue) ||
                !string.Equals(field.Value, rightValue, StringComparison.Ordinal))
                return false;

        for (var i = 0; i < left.Children.Count; i++)
            if (!Equals(left.Children[i], right.Children[i]))
                return false;

        return true;
    }
}
