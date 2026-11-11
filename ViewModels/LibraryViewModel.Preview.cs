using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Library: side preview pane (metadata, generation fields, notes).</summary>
public partial class LibraryViewModel
{
    private async Task LoadPreviewAsync(AssetItem? item)
    {
        var epoch = Interlocked.Increment(ref _previewEpoch);
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _previewEpoch)
            {
                return;
            }

            ClearGenerationPreview();
            if (item is null || item.IsFolderHeader)
            {
                ClearPreview();
            }
        });
        if (item is null || item.IsFolderHeader || epoch != _previewEpoch)
        {
            return;
        }

        var asset = await _catalog.GetAssetByIdAsync(item.Id);
        if (epoch != _previewEpoch)
        {
            return;
        }

        if (asset is null)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (epoch == _previewEpoch)
                {
                    ClearPreview();
                }
            });
            return;
        }

        var targets = _selection.Count > 0 ? _selection.ToList() : [item];
        var perAsset = new List<IReadOnlyList<AssignedTag>>();
        var ratings = new List<int?>();
        foreach (var target in targets)
        {
            perAsset.Add(await _catalog.GetAssignedTagsAsync(target.Id));
            var row = await _catalog.GetAssetByIdAsync(target.Id);
            ratings.Add(row?.Rating);
        }

        if (epoch != _previewEpoch)
        {
            return;
        }

        var total = targets.Count;
        var multi = SelectCommandBar.ShowMultiSelectPreview(total);
        var union = new Dictionary<string, (AssignedTag Sample, HashSet<string> AssetIds, HashSet<TagSource> Sources)>(
            StringComparer.Ordinal);
        for (var i = 0; i < perAsset.Count; i++)
        {
            var assetId = targets[i].Id;
            foreach (var tag in perAsset[i])
            {
                if (!union.TryGetValue(tag.TagId, out var current))
                {
                    union[tag.TagId] = (tag, new HashSet<string>(StringComparer.Ordinal) { assetId }, [tag.Source]);
                }
                else
                {
                    current.Sources.Add(tag.Source);
                    current.AssetIds.Add(assetId);
                    union[tag.TagId] = (current.Sample, current.AssetIds, current.Sources);
                }
            }
        }

        var existing = await _catalog.GetTagsAsync();
        var implications = await _catalog.GetImplicationsAsync();
        var aliases = await _catalog.GetAliasesAsync();
        var suggestions = PromptTagSuggester.Suggest(asset.Prompt, existing);
        var hydrateOnOpen = AccessService.WouldHydrateOnOpen(asset.Path);
        var apiOnly = AssetItemMapper.IsApiOnly(asset);
        var originalOk = GalleryMedia.CanShowPreview(asset.Path);
        var cloudish = GalleryMedia.IsLiveOnlineOnly(
            asset.IsOnlineOnly, hydrateOnOpen, apiOnly, originalOk);
        var previewPath = multi
            ? null
            : GalleryMedia.OverlayStillPath(
                asset.IsOrphan, cloudish, originalOk, asset.Path, item.ThumbPath, null);
        if (!GalleryMedia.CanShowPreview(previewPath))
        {
            previewPath = null;
        }

        var canEditNotes = total == 1;
        var ratingMixed = SelectCommandBar.RatingIsMixed(ratings);
        var ratingValue = SelectCommandBar.RatingDisplayValue(ratings);
        var multiDetail = multi
            ? SelectCommandBar.MultiSelectDetail(
                targets.Select(t => t.FileName).ToList(),
                targets.Select(t => t.FileSize).ToList())
            : "";
        var generation = multi
            ? GenerationFields.ForPreview(null, null, null, null)
            : GenerationFields.ForPreview(
                asset.Model, asset.Prompt, asset.NegativePrompt, asset.Seed);
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _previewEpoch)
            {
                return;
            }

            CanEditNotes = canEditNotes;
            IsMultiSelectPreview = multi;
            ShowSinglePreviewFileName = total == 1;
            AddTagButtonLabel = multi ? "Add tags to all" : "Add tags";
            MultiSelectDetail = multiDetail;
            PreviewRatingMixed = ratingMixed;
            RatingCaption = SelectCommandBar.RatingCaption(ratingMixed);
            // Assign targets before enabling rating so CanEditRating never races a stale set.
            _previewTargets = targets;
            CanEditRating = PreviewPersist.ShouldEnableRatingEdit(
                total,
                PreviewPersist.PreviewTargetsMatchSelection(
                    targets.Select(t => t.Id).ToList(),
                    _selection.Select(s => s.Id).ToList()));
            PreviewPath = previewPath;
            IsVideoPreview = !multi && !cloudish && asset.Kind == AssetKind.Video && previewPath is not null;
            IsImagePreview = !multi && previewPath is not null &&
                (cloudish || asset.Kind is AssetKind.Image or AssetKind.Gif);
            ShowPreviewPlaceholder = !multi && GalleryMedia.ShowPlaceholderTile(
                cloudish, asset.IsOrphan, previewPath is not null);
            PreviewPlaceholderKind = asset.Kind;
            ApplyGenerationPreview(generation);
            PreviewFileName = multi
                ? SelectMode.Summary(true, total)
                : asset.FileName;
            SetPreviewNotes(canEditNotes ? asset.Notes : null, ratingValue, canEditNotes ? asset.Id : null);

            AssignedTags.Clear();
            ImpliedAssignedTags.Clear();
            var implicationEdges = implications
                .Select(i => (i.TagId, i.ImpliedTagId))
                .ToList();
            var aliasesByIdeal = aliases
                .GroupBy(a => a.IdealTagId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g
                        .Select(a => existing.FirstOrDefault(t => t.Id == a.AliasTagId)?.Name ?? "")
                        .Where(n => n.Length > 0)
                        .ToList(),
                    StringComparer.Ordinal);
            var nameById = existing.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
            // Build reverse implication map once per preview — not per chip.
            var reverseImplications = ImpliedNearest.BuildReverse(implicationEdges);
            var appliedIds = union.Values
                .Where(v => !AssignedTagPartition.IsImpliedOnly(v.Sources))
                .Select(v => v.Sample.TagId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entry in union.Values
                .OrderBy(v => v.Sample.TagName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Sample.TagName, StringComparer.Ordinal))
            {
                var tag = entry.Sample;
                var groups = tag.ParentNames.Count > 0 ? $" ({string.Join(", ", tag.ParentNames)})" : "";
                var appliedCount = entry.AssetIds.Count;
                var isPartial = total > 1 && appliedCount < total;
                var impliedOnly = AssignedTagPartition.IsImpliedOnly(entry.Sources);
                var source = AssignedTagPartition.EffectiveSource(entry.Sources, tag.Source);
                // One IsAlsoImplied / NearestSourceId walk per chip (exclude self for also-implied).
                string? nearestId = null;
                var alsoImplied = false;
                if (impliedOnly)
                {
                    nearestId = ImpliedNearest.NearestSourceId(
                        tag.TagId, appliedIds, reverseImplications);
                }
                else
                {
                    var others = appliedIds
                        .Where(id => !string.Equals(id, tag.TagId, StringComparison.Ordinal))
                        .ToHashSet(StringComparer.Ordinal);
                    nearestId = ImpliedNearest.NearestSourceId(
                        tag.TagId, others, reverseImplications);
                    alsoImplied = nearestId is not null;
                }

                var nearestName = nearestId is not null && nameById.TryGetValue(nearestId, out var n)
                    ? n
                    : null;
                var item = new AssignedTagItem
                {
                    TagId = tag.TagId,
                    TagName = tag.TagName,
                    Display = tag.TagName + groups,
                    Source = source,
                    SourceLabel = entry.Sources.Count == 1
                        ? (source == TagSource.Implied ? "Implied" : source.ToString())
                        : "Mixed",
                    EffectiveColor = tag.EffectiveColor,
                    IsPartial = isPartial,
                    AppliedCount = appliedCount,
                    SelectionTotal = total,
                    AssetIds = entry.AssetIds.ToList(),
                    IsChipSelected = false,
                    CountLabel = isPartial ? TagChipVisual.PartialBadge(appliedCount, total) : "",
                    NearestImpliedByName = nearestName,
                    IsAlsoImplied = alsoImplied,
                    AliasNames = aliasesByIdeal.GetValueOrDefault(tag.TagId) ?? []
                };

                // T8 Option A: applied chips first; implied-only under their own subsection.
                // Mixed Manual+Implied must stay in Assigned — never the Implied strip.
                if (impliedOnly)
                {
                    ImpliedAssignedTags.Add(item);
                }
                else
                {
                    AssignedTags.Add(item);
                }
            }

            HasImpliedAssignedTags = ImpliedAssignedTags.Count > 0;
            AssignedTagsSummary = AssignedTags.Count == 0 && ImpliedAssignedTags.Count == 0
                ? "No tags assigned"
                : string.Join(", ", AssignedTags.Concat(ImpliedAssignedTags).Select(t => t.TagName));
            ShowAssignedTagsSummary = AssignedTags.Count == 0 && ImpliedAssignedTags.Count == 0;

            // Prompt suggestion chips land in audit (k); keep Suggest engine warm only.
            Suggestions.Clear();
            if (!multi)
            {
                foreach (var suggestion in suggestions)
                {
                    if (AssignedTags.Concat(ImpliedAssignedTags)
                        .Any(t => t.TagName.Equals(suggestion.Token, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    Suggestions.Add(suggestion);
                }
            }

            RefreshPendingChrome();
        });
    }

    private void ApplyGenerationPreview(GenerationFields.Snapshot generation)
    {
        PreviewModel = generation.Model;
        PreviewPrompt = generation.Prompt;
        PreviewNegative = generation.Negative;
        PreviewSeed = generation.Seed;
        ShowGeneration = generation.HasAny;
    }

    private void ClearGenerationPreview() => ApplyGenerationPreview(GenerationFields.ForPreview(null, null, null, null));

    private void SetPreviewNotes(string? notes, double rating, string? previewAssetId)
    {
        _applyingPreview = true;
        try
        {
            _previewAssetId = previewAssetId;
            PreviewNotes = notes;
            PreviewRating = rating;
        }
        finally
        {
            _applyingPreview = false;
        }
    }

    private void ClearPreview()
    {
        AssignedTags.Clear();
        ImpliedAssignedTags.Clear();
        HasImpliedAssignedTags = false;
        Suggestions.Clear();
        _previewTargets = [];
        AssignedTagsSummary = "";
        ShowAssignedTagsSummary = true;
        IsImagePreview = false;
        IsVideoPreview = false;
        ShowPreviewPlaceholder = false;
        IsMultiSelectPreview = false;
        ShowSinglePreviewFileName = false;
        MultiSelectDetail = "";
        ShowRemoveTags = false;
        ShowRemoveTagsLink = false;
        ShowTagsMoreMenu = false;
        AssignedTagsHeaderSuffix = "";
        PreviewRatingMixed = false;
        RatingCaption = SelectCommandBar.RatingCaption(false);
        CanEditRating = false;
        PreviewPath = null;
        ClearGenerationPreview();
        PreviewFileName = null;
        SetPreviewNotes(null, 0, null);
    }
}
