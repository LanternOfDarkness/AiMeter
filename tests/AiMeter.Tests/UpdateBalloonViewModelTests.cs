using AiMeter.Managers;
using AiMeter.Services.Updates;
using AiMeter.Tests.Fakes;
using AiMeter.ViewModels;
using FluentAssertions;
using Xunit;

namespace AiMeter.Tests;

public class UpdateBalloonViewModelTests
{
    private static ReleaseInfo Release(string notes) => new(
        new AppVersion(9, 9, 9),
        "v9.9.9",
        "https://github.com/LanternOfDarkness/AiMeter/releases/tag/v9.9.9",
        notes,
        new ReleaseAsset(
            "AiMeter.exe",
            new Uri("https://github.com/LanternOfDarkness/AiMeter/releases/download/v9.9.9/AiMeter.exe"),
            1024),
        null,
        null);

    // The balloon VM only needs a callback and the manager for these tests; constructing a
    // real UpdateWindow would require WPF. Update() tolerates the missing window.
    private static (UpdateBalloonViewModel ViewModel, FakeUpdateManager Manager) Create()
    {
        var manager = new FakeUpdateManager();
        return (new UpdateBalloonViewModel(manager, null!, new FakeUiDispatcher()), manager);
    }

    [Fact]
    public void Initialize_sets_the_version_text()
    {
        var (vm, _) = Create();

        vm.Initialize(Release("Notes"), () => { });

        vm.VersionText.Should().Be("AiMeter v9.9.9 is available");
    }

    [Fact]
    public void Initialize_collapses_whitespace_in_the_notes_summary()
    {
        var (vm, _) = Create();

        vm.Initialize(Release("  First\n\nline\tsecond  "), () => { });

        vm.NotesSummary.Should().Be("First line second");
    }

    [Fact]
    public void Notes_summary_is_truncated_to_180_chars_with_an_ellipsis()
    {
        var (vm, _) = Create();
        var notes = new string('x', 300);

        vm.Initialize(Release(notes), () => { });

        vm.NotesSummary.Should().HaveLength(181);
        vm.NotesSummary.Should().Be(new string('x', 180) + "\u2026");
    }

    [Fact]
    public void Notes_summary_falls_back_when_the_release_has_no_notes()
    {
        var (vm, _) = Create();

        vm.Initialize(Release("   "), () => { });

        vm.NotesSummary.Should().Be("A new version is available.");
    }

    [Fact]
    public void Update_closes_the_balloon()
    {
        var (vm, _) = Create();
        var closeCount = 0;
        vm.Initialize(Release("Notes"), () => closeCount++);

        vm.UpdateCommand.Execute(null);

        closeCount.Should().Be(1);
    }

    [Fact]
    public void Later_dismisses_the_notification_and_closes_the_balloon()
    {
        var (vm, manager) = Create();
        var closeCount = 0;
        vm.Initialize(Release("Notes"), () => closeCount++);

        vm.LaterCommand.Execute(null);

        manager.DismissCallCount.Should().Be(1);
        closeCount.Should().Be(1);
    }

    [Fact]
    public void Commands_before_initialize_do_nothing_and_do_not_throw()
    {
        var (vm, manager) = Create();
        var closeCount = 0;

        var act = () =>
        {
            vm.UpdateCommand.Execute(null);
            vm.LaterCommand.Execute(null);
        };

        act.Should().NotThrow();
        manager.DismissCallCount.Should().Be(0);
        closeCount.Should().Be(0);
        vm.VersionText.Should().BeEmpty();
    }
}
