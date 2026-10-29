using Kazuko.Core.PriceBoard;
using Kazuko.Modules.PriceBoard;
using Xunit;

namespace Kazuko.Modules.PriceBoard.Tests;

public sealed class TvDisplayFlowTests
{
    private static readonly TvMonitor Primary = new("primary", "Máy tính", 0, 0, 1920, 1080, true);
    private static readonly TvMonitor Tv = new("tv", "TV cửa hàng", 1920, 0, 1920, 1080, false);

    [Fact]
    public async Task Choosing_a_Miracast_device_only_opens_the_native_picker_and_does_not_start_projection()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            vm.SelectedMonitor = Primary; // A prior choice must not be reused for a new cast session.
            vm.ConnectMiracastCommand.Execute(null);
            Assert.Equal(1, displays.PickerCalls);
            Assert.Equal(0, board.ApplyCalls);
            Assert.Null(board.Opened);
            Assert.Null(vm.SelectedMonitor);
            Assert.False(vm.OpenDisplayCommand.CanExecute(null));
            Assert.False(vm.Enabled);
            Assert.Contains("Windows", vm.DisplayConnectionMessage);
        }
    }

    [Fact]
    public async Task A_newly_connected_TV_appears_and_projection_waits_for_explicit_target_selection()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv];
            vm.RefreshMonitors();
            Assert.Equal(2, vm.Monitors.Count);
            Assert.Null(vm.SelectedMonitor);
            Assert.Null(board.Opened);
            vm.SelectedMonitor = vm.Monitors.Single(x => x.Device == Tv.Device);
            vm.OpenDisplayCommand.Execute(null);
            Assert.Equal(Tv, board.Opened);
            Assert.True(board.Settings.Enabled);
            Assert.Equal(Tv.Device, board.Settings.MonitorDevice);
        }
    }

    [Fact]
    public async Task Refresh_preserves_the_chosen_target_even_when_ComboBox_sends_null_during_reset()
    {
        var (vm, _, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv];
            vm.RefreshMonitors();
            vm.SelectedMonitor = Tv;
            vm.Monitors.CollectionChanged += (_, _) => vm.SelectedMonitor = null;
            var resized = Tv with { Width = 3840, Height = 2160 };
            displays.Monitors = [Primary, resized];
            vm.RefreshMonitors();
            Assert.Equal(resized, vm.SelectedMonitor);
        }
    }

    [Fact]
    public async Task Disconnect_clears_the_target_and_does_not_fall_back_to_the_primary_monitor()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv]; vm.RefreshMonitors(); vm.SelectedMonitor = Tv;
            displays.Monitors = [Primary]; vm.RefreshMonitors();
            Assert.Null(vm.SelectedMonitor);
            Assert.False(vm.OpenDisplayCommand.CanExecute(null));
            vm.OpenDisplayCommand.Execute(null);
            Assert.Equal(0, board.ApplyCalls);
            Assert.Null(board.Opened);
            Assert.Contains("ngắt", vm.DisplayConnectionMessage);
        }
    }

    [Fact]
    public async Task A_stale_target_is_rechecked_before_any_settings_are_applied()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv]; vm.RefreshMonitors(); vm.SelectedMonitor = Tv;
            displays.Monitors = [Primary]; // No Windows display event delivered yet.
            vm.OpenDisplayCommand.Execute(null);
            Assert.Equal(0, board.ApplyCalls);
            Assert.Null(board.Opened);
            Assert.Contains("ngắt", vm.Error);
        }
    }

    [Fact]
    public async Task Disconnect_during_an_async_apply_cannot_redirect_projection_to_another_screen()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv]; vm.RefreshMonitors(); vm.SelectedMonitor = Tv;
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            board.ApplyWait = () => pending.Task;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.IsBusy) && !vm.IsBusy) finished.TrySetResult();
            };
            vm.OpenDisplayCommand.Execute(null);
            Assert.True(vm.IsBusy);
            displays.Monitors = [Primary];
            pending.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, board.ApplyCalls);
            Assert.Null(board.Opened);
            Assert.Contains("ngắt", vm.Error);
        }
    }

    [Fact]
    public async Task Projection_uses_current_screen_bounds_instead_of_the_old_enumeration()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Monitors = [Primary, Tv]; vm.RefreshMonitors(); vm.SelectedMonitor = Tv;
            var moved = Tv with { Left = -3840, Width = 3840 };
            displays.Monitors = [Primary, moved];
            vm.OpenDisplayCommand.Execute(null);
            Assert.Equal(moved, board.Opened);
        }
    }

    [Fact]
    public async Task Missing_saved_screen_does_not_choose_a_different_device_automatically()
    {
        var board = new Board { Settings = new(MonitorDevice: "disconnected-tv") };
        var displays = new Displays();
        using var vm = new PriceBoardTvSettingsViewModel(board, displays);
        await vm.InitializeAsync();
        Assert.Null(vm.SelectedMonitor);
        Assert.False(vm.OpenDisplayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Windows_picker_failure_is_reported_without_changing_LAN_or_opening_a_screen()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            displays.Fail = true;
            vm.ConnectMiracastCommand.Execute(null);
            Assert.Contains("Windows + K", vm.Error);
            Assert.False(board.Settings.AllowLan);
            Assert.Equal(0, board.ApplyCalls);
            Assert.Null(board.Opened);
        }
    }

    [Fact]
    public async Task Projection_mode_is_chosen_in_Windows_without_starting_the_board()
    {
        var (vm, board, displays) = await Create();
        using (vm)
        {
            vm.ProjectionModeCommand.Execute(null);
            Assert.Equal(1, displays.ProjectionCalls);
            Assert.Contains("Extend", vm.DisplayConnectionMessage);
            Assert.Equal(0, board.ApplyCalls);
            Assert.Null(board.Opened);
        }
    }

    private static async Task<(PriceBoardTvSettingsViewModel, Board, Displays)> Create()
    {
        var board = new Board(); var displays = new Displays();
        var vm = new PriceBoardTvSettingsViewModel(board, displays);
        await vm.InitializeAsync();
        return (vm, board, displays);
    }
    private sealed class Displays : ITvDisplayService
    {
        public IReadOnlyList<TvMonitor> Monitors = [Primary];
        public int PickerCalls, ProjectionCalls;
        public bool Fail;
        public IReadOnlyList<TvMonitor> GetMonitors() => Monitors;
        public void ShowMiracastDevicePicker()
        {
            if (Fail) throw new InvalidOperationException("Hãy nhấn Windows + K để mở bảng chọn.");
            PickerCalls++;
        }
        public void ShowProjectionModePicker() => ProjectionCalls++;
    }
    private sealed class Board : IPriceBoardTvSettingsService
    {
        public event Action? Changed;
        public string LocalUrl => "";
        public string LanAddresses => "LAN đang tắt.";
        public PriceBoardTvSettings Settings { get; set; } = new();
        public IReadOnlyList<GoldPriceRecord> Prices => [];
        public int ApplyCalls;
        public TvMonitor? Opened;
        public Func<Task>? ApplyWait;
        public Task InitializeAsync() => Task.CompletedTask;
        public async Task ApplyAsync(PriceBoardTvSettings settings)
        {
            ApplyCalls++;
            if (ApplyWait is not null) await ApplyWait();
            Settings = settings; Changed?.Invoke();
        }
        public Task OpenDisplayAsync(TvMonitor monitor) { Opened = monitor; return Task.CompletedTask; }
    }
}
