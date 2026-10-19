using Palace.Models;

namespace Palace.Helpers;

/// <summary>Status-line wording after batch create / find-or-create on the Tags board.</summary>
public static class TagCreateCopy
{
    public static string Describe(TagCreateBatchResult result, string? suffix = null)
    {
        if (result.Created == 0 && result.Existed == 0)
        {
            return "Enter one or more tag names.";
        }

        if (result.Created == 0 && result.Existed > 0)
        {
            var name = result.Tags.LastOrDefault()?.Name;
            var text = result.Existed == 1 && !string.IsNullOrEmpty(name)
                ? $"“{name}” already exists — selected it{suffix}."
                : $"{result.Existed} already existed — selected the last one{suffix}.";
            if (result.MembershipRejected > 0)
            {
                text = text.TrimEnd('.') + $", could not nest {result.MembershipRejected}.";
            }

            return text;
        }

        var mixed = $"Created {result.Created}";
        if (result.Existed > 0)
        {
            mixed += $", already existed {result.Existed}";
        }

        mixed += suffix ?? "";
        if (result.MembershipRejected > 0)
        {
            mixed += $", could not nest {result.MembershipRejected}";
        }

        return mixed + ".";
    }
}
