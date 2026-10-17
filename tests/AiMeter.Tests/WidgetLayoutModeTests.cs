using AiMeter.Models;
using AiMeter.ViewModels;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

/// <summary>
/// Covers the pure layout-mode cycle behind <c>ToggleLayout</c>, including the
/// Deskband-skipping behavior on OSes that can't host it.
/// </summary>
public class WidgetLayoutModeTests
{
    [Fact]
    public void NextLayoutMode_cycles_Compact_to_Taskbar()
    {
        WidgetViewModel.NextLayoutMode(WidgetLayoutMode.Compact, deskbandSupported: true)
            .Should().Be(WidgetLayoutMode.Taskbar);
    }

    [Fact]
    public void NextLayoutMode_cycles_Taskbar_to_Deskband_when_supported()
    {
        WidgetViewModel.NextLayoutMode(WidgetLayoutMode.Taskbar, deskbandSupported: true)
            .Should().Be(WidgetLayoutMode.Deskband);
    }

    [Fact]
    public void NextLayoutMode_cycles_Taskbar_to_Compact_when_deskband_unsupported()
    {
        WidgetViewModel.NextLayoutMode(WidgetLayoutMode.Taskbar, deskbandSupported: false)
            .Should().Be(WidgetLayoutMode.Compact);
    }

    [Fact]
    public void NextLayoutMode_cycles_Deskband_to_Compact()
    {
        WidgetViewModel.NextLayoutMode(WidgetLayoutMode.Deskband, deskbandSupported: true)
            .Should().Be(WidgetLayoutMode.Compact);
    }
}
