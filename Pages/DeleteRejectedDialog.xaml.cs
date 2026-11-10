using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.ViewModels;

namespace Palace.Pages;

public sealed partial class DeleteRejectedDialog : ContentDialog
{
    private readonly DeleteRejectedPrompt _prompt;

    public DeleteRejectedChoice? Choice { get; private set; }

    public DeleteRejectedDialog(DeleteRejectedPrompt prompt)
    {
        _prompt = prompt;
        InitializeComponent();
        PrimaryButtonText = "Cancel"; // replaced in SyncPrimary
        // Automation close button id
        CloseButtonCommand = null;

        var viewSecondary = $"{prompt.ViewCount} files · {DeleteRejected.FormatSize(prompt.ViewBytes)}";
        RadDeleteRejectedScopeView.Content = $"{prompt.ViewScopeLabel}  ({viewSecondary})";

        var libSecondary = $"{prompt.LibraryCount} files · {DeleteRejected.FormatSize(prompt.LibraryBytes)}";
        if (prompt.NetworkCount > 0 && !string.IsNullOrWhiteSpace(prompt.NetworkFolderLabel))
        {
            libSecondary += $" · adds {prompt.NetworkCount} in {prompt.NetworkFolderLabel}";
        }

        RadDeleteRejectedScopeLibrary.Content = $"Whole library  ({libSecondary})";

        if (prompt.DefaultScope == DeleteRejectedScope.ThisView)
        {
            RadDeleteRejectedScopeView.IsChecked = true;
        }
        else
        {
            RadDeleteRejectedScopeLibrary.IsChecked = true;
        }

        if (prompt.DefaultDestination == DeleteRejectedDestination.Permanent)
        {
            RadDeleteRejectedPermanent.IsChecked = true;
        }
        else
        {
            RadDeleteRejectedRecycle.IsChecked = true;
        }

        SyncChrome();
    }

    private void OnOptionsChanged(object sender, RoutedEventArgs e) => SyncChrome();

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Choice = new DeleteRejectedChoice
        {
            Scope = RadDeleteRejectedScopeLibrary.IsChecked == true
                ? DeleteRejectedScope.WholeLibrary
                : DeleteRejectedScope.ThisView,
            Destination = RadDeleteRejectedPermanent.IsChecked == true
                ? DeleteRejectedDestination.Permanent
                : DeleteRejectedDestination.RecycleBin
        };
    }

    private void SyncChrome()
    {
        var scope = RadDeleteRejectedScopeLibrary.IsChecked == true
            ? DeleteRejectedScope.WholeLibrary
            : DeleteRejectedScope.ThisView;
        var dest = RadDeleteRejectedPermanent.IsChecked == true
            ? DeleteRejectedDestination.Permanent
            : DeleteRejectedDestination.RecycleBin;

        var count = scope == DeleteRejectedScope.WholeLibrary ? _prompt.LibraryCount : _prompt.ViewCount;
        var bytes = scope == DeleteRejectedScope.WholeLibrary ? _prompt.LibraryBytes : _prompt.ViewBytes;
        var folders = scope == DeleteRejectedScope.WholeLibrary ? _prompt.FolderCountLibrary : _prompt.FolderCountView;
        var network = scope == DeleteRejectedScope.WholeLibrary ? _prompt.NetworkCount : 0;

        var actionable = dest == DeleteRejectedDestination.RecycleBin
            ? Math.Max(0, count - network)
            : count;
        var actionableBytes = dest == DeleteRejectedDestination.RecycleBin && network > 0 && count > 0
            ? Math.Max(0, bytes - _prompt.NetworkBytes)
            : bytes;

        var scopePhrase = scope == DeleteRejectedScope.WholeLibrary
            ? $"from {folders} _rejected folders in {_prompt.SourceCountLibrary} sources"
            : $"from {folders} _rejected folders";

        TxtDeleteRejectedSubtitle.Text =
            $"{count} rejected files · {DeleteRejected.FormatSize(bytes)} {scopePhrase}";

        var showNetwork = dest == DeleteRejectedDestination.RecycleBin && network > 0;
        TxtDeleteRejectedNetworkSkip.Text = DeleteRejected.NetworkSkipNote(network);
        TxtDeleteRejectedNetworkSkip.Visibility = showNetwork ? Visibility.Visible : Visibility.Collapsed;

        var permanent = dest == DeleteRejectedDestination.Permanent;
        BrdPermanentWarn.Visibility = permanent ? Visibility.Visible : Visibility.Collapsed;
        if (permanent)
        {
            TxtDeleteRejectedPermanentWarn.Text =
                $"Can’t be undone. {count} files ({DeleteRejected.FormatSize(bytes)}) are erased from disk and removed from the catalog, with their tags and ratings. Undo and the Recycle Bin won’t bring them back.";
        }

        PrimaryButtonText = DeleteRejected.ConfirmPrimaryLabel(dest, actionable);
        // WinUI ContentDialog has no direct PrimaryButton style swap; danger is conveyed by copy.
        _ = actionableBytes;
    }
}
