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
        foreach (var target in targets)
        {
            perAsset.Add(await _catalog.GetAssignedTagsAsync(target.Id));
        }

        if (epoch != _previewEpoch)
        {
            return;
        }

        var total = targets.Count;
        var union = new Dictionary<string, (AssignedTag Sample, int Count, HashSet<TagSource> Sources)>(StringComparer.Ordinal);
        foreach (var list in perAsset)
        {
            foreach (var tag in list)
            {
                if (!union.TryGetValue(tag.TagId, out var current))
                {
                    union[tag.TagId] = (tag, 1, [tag.Source]);
                }
                else
                {
                    current.Sources.Add(tag.Source);
                    union[tag.TagId] = (current.Sample, current.Count + 1, current.Sources);
                }
            }
        }

        var existing = await _catalog.GetTagsAsync();
        var suggestions = PromptTagSuggester.Suggest(asset.Prompt, existing);
        var hydrateOnOpen = AccessService.WouldHydrateOnOpen(asset.Path);
        var apiOnly = AssetItemMapper.IsApiOnly(asset);
        var originalOk = GalleryMedia.CanShowPreview(asset.Path);
        var cloudish = GalleryMedia.IsLiveOnlineOnly(
            asset.IsOnlineOnly, hydrateOnOpen, apiOnly, originalOk);
        var previewPath = GalleryMedia.OverlayStillPath(
            asset.IsOrphan, cloudish, originalOk, asset.Path, item.ThumbPath, null);
        if (!GalleryMedia.CanShowPreview(previewPath))
        {
            previewPath = null;
        }

        var canEdit = total == 1;
        var generation = GenerationFields.ForPreview(
            asset.Model, asset.Prompt, asset.NegativePrompt, asset.Seed);
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _previewEpoch)
            {
                return;
            }

            CanEditNotes = canEdit;
            _previewTargets = targets;
            PreviewPath = previewPath;
            IsVideoPreview = !cloudish && asset.Kind == AssetKind.Video && previewPath is not null;
            IsImagePreview = previewPath is not null &&
                (cloudish || asset.Kind is AssetKind.Image or AssetKind.Gif);
            ShowPreviewPlaceholder = GalleryMedia.ShowPlaceholderTile(
                cloudish, asset.IsOrphan, previewPath is not null);
            PreviewPlaceholderKind = asset.Kind;
            ApplyGenerationPreview(generation);
            PreviewFileName = asset.FileName;
            SetPreviewNotes(asset.Notes, asset.Rating ?? 0, asset.Id);

            AssignedTags.Clear();
            foreach (var entry in union.Values
                .OrderBy(v => v.Sample.TagName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Sample.TagName, StringComparer.Ordinal))
            {
                var tag = entry.Sample;
                var groups = tag.ParentNames.Count > 0 ? $" ({string.Join(", ", tag.ParentNames)})" : "";
                var isPartial = total > 1 && entry.Count < total;
                var source = entry.Sources.Count == 1 ? entry.Sources.First() : tag.Source;
                AssignedTags.Add(new AssignedTagItem
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
                    CountLabel = isPartial ? $"{entry.Count}/{total}" : ""
                });
            }

            AssignedTagsSummary = AssignedTags.Count == 0
                ? "No tags assigned"
                : string.Join(", ", AssignedTags.Select(t => t.TagName));

            Suggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                if (AssignedTags.Any(t => t.TagName.Equals(suggestion.Token, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Suggestions.Add(suggestion);
            }

            RebuildAssignPanel();
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
        Suggestions.Clear();
        _previewTargets = [];
        AssignedTagsSummary = "";
        IsImagePreview = false;
        IsVideoPreview = false;
        ShowPreviewPlaceholder = false;
        PreviewPath = null;
        ClearGenerationPreview();
        PreviewFileName = null;
        SetPreviewNotes(null, 0, null);
    }
}
