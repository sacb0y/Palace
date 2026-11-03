using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class PreviewPaneSettingsTests
{
    public PreviewPaneSettingsTests()
    {
        PreviewPaneSettings.Apply(
            PreviewPaneSettings.DefaultMode,
            userShow: true,
            LibraryColumnLayout.PreviewDefaultWidth);
    }

    [Fact]
    public void NormalizeMode_DefaultsAutomatic()
    {
        Assert.Equal(PreviewPaneSettings.ModeAutomatic, PreviewPaneSettings.NormalizeMode(null));
        Assert.Equal(PreviewPaneSettings.ModeAutomatic, PreviewPaneSettings.NormalizeMode("nope"));
        Assert.Equal(PreviewPaneSettings.ModeShow, PreviewPaneSettings.NormalizeMode("show"));
        Assert.Equal(PreviewPaneSettings.ModeHide, PreviewPaneSettings.NormalizeMode("Hide"));
    }

    [Fact]
    public void Apply_HideForcesUserShowFalse_ShowForcesTrue()
    {
        PreviewPaneSettings.Apply(PreviewPaneSettings.ModeHide, userShow: true, 400);
        Assert.False(PreviewPaneSettings.UserShow);
        Assert.False(PreviewPaneSettings.AutoHideEnabled);
        Assert.Equal(400, PreviewPaneSettings.UserWidth);

        PreviewPaneSettings.Apply(PreviewPaneSettings.ModeShow, userShow: false, 300);
        Assert.True(PreviewPaneSettings.UserShow);
        Assert.False(PreviewPaneSettings.AutoHideEnabled);

        PreviewPaneSettings.Apply(PreviewPaneSettings.ModeAutomatic, userShow: false, 320);
        Assert.False(PreviewPaneSettings.UserShow);
        Assert.True(PreviewPaneSettings.AutoHideEnabled);
        Assert.Equal(320, PreviewPaneSettings.UserWidth);
    }

    [Fact]
    public void ParseWidth_ClampsAndDefaults()
    {
        Assert.Equal(280, PreviewPaneSettings.ParseWidth(10));
        Assert.Equal(560, PreviewPaneSettings.ParseWidth(999));
        Assert.Equal(340, PreviewPaneSettings.ParseWidth(null));
        Assert.Equal(400, PreviewPaneSettings.ParseWidth("400"));
    }

    [Fact]
    public void ToState_ClearsSessionFlags()
    {
        PreviewPaneSettings.Apply(PreviewPaneSettings.ModeAutomatic, userShow: true, 420);
        var state = PreviewPaneSettings.ToState();
        Assert.True(state.UserShow);
        Assert.False(state.AutoHidden);
        Assert.False(state.OverlayOpen);
        Assert.False(state.Pinned);
        Assert.Equal(420, state.UserWidth);
    }
}
