using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class AttendancePeriodTests
{
    private static AttendanceTabViewModel View(IHumanResourcesRepository repository)
    {
        var session = new CurrentSessionAccessor();
        session.Set(new(1, "admin", "Admin", "ADMIN", 1, "CN1", "Chi nhánh 1"));
        return new(repository, session, new ShiftEditorFake());
    }

    [Fact]
    public async Task Empty_period_is_saved_before_display_and_survives_new_view_and_refresh()
    {
        var repo = DispatchProxy.Create<IHumanResourcesRepository, AttendanceRepositoryFake>();
        var fake = (AttendanceRepositoryFake)repo;
        var first = View(repo);
        await first.EnsureLoadedAsync();
        Assert.Empty(first.Periods);
        first.AddPeriodCommand.Execute(null);
        var created = Assert.Single(first.Periods);
        Assert.False(created.HasData);
        Assert.Equal(DateTime.Today.Month, created.Month);
        var reopened = View(repo);
        await reopened.EnsureLoadedAsync();
        Assert.Equal(created.DisplayName, Assert.Single(reopened.Periods).DisplayName);
        reopened.RefreshCommand.Execute(null);
        Assert.Single(reopened.Periods);
        reopened.DeletePeriodCommand.Execute(null);
        Assert.Empty(fake.Months);
        var afterDelete = View(repo);
        await afterDelete.EnsureLoadedAsync();
        Assert.Empty(afterDelete.Periods);
        Assert.False(afterDelete.HasRows);
    }

    [Fact]
    public async Task Failed_creation_does_not_add_an_unsaved_period_and_loading_can_retry()
    {
        var repo = DispatchProxy.Create<IHumanResourcesRepository, AttendanceRepositoryFake>();
        var fake = (AttendanceRepositoryFake)repo;
        fake.FailLoad = true;
        var vm = View(repo);
        await vm.EnsureLoadedAsync();
        Assert.True(vm.HasError);
        fake.FailLoad = false;
        await vm.EnsureLoadedAsync();
        Assert.False(vm.HasError);
        fake.FailCreate = true;
        vm.AddPeriodCommand.Execute(null);
        Assert.Empty(vm.Periods);
        Assert.True(vm.HasError);
        fake.FailCreate = false;
        vm.AddPeriodCommand.Execute(null);
        Assert.Single(vm.Periods);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Backend_rejection_keeps_period_with_records_visible()
    {
        var repo = DispatchProxy.Create<IHumanResourcesRepository, AttendanceRepositoryFake>();
        var fake = (AttendanceRepositoryFake)repo;
        fake.Months.Add(new(2026, 11, true));
        var vm = View(repo);
        await vm.EnsureLoadedAsync();
        vm.DeletePeriodCommand.Execute(null);
        Assert.Single(vm.Periods);
        Assert.Contains("có dữ liệu", vm.ErrorMessage);
    }

    private sealed class ShiftEditorFake : IShiftEditorService
    {
        public WorkShiftItem? ShowCreate() => null;
        public void ShowManage() { }
    }

    [Fact]
    public async Task Inactive_shift_remains_visible_when_month_has_historical_records()
    {
        var repo = DispatchProxy.Create<IHumanResourcesRepository, AttendanceRepositoryFake>();
        var fake = (AttendanceRepositoryFake)repo;
        fake.Months.Add(new(2026,10,true));
        fake.Employees.Add(new(1,"NV1","Nhân viên",null,"",null,"","",null,EmployeeStatusCodes.Working,true));
        fake.Shifts.Add(new(10,"Ca đã ngừng",null,null,1,false));
        fake.Shifts.Add(new(11,"Ca ngừng không có công",null,null,2,false));
        fake.Records.Add(new(1,10,new DateTime(2026,10,5),AttendanceStatusCodes.Work));
        var vm = View(repo); await vm.EnsureLoadedAsync();
        var row = Assert.Single(Assert.Single(vm.Groups).ShiftRows);
        Assert.Equal(10,row.ShiftId);
        Assert.Equal(AttendanceStatusCodes.Work,row.Cells[4].StatusCode);
    }
}

public class AttendanceRepositoryFake : DispatchProxy
{
    public List<EmployeeListItem> Employees { get; } = [];
    public List<WorkShiftItem> Shifts { get; } = [];
    public List<AttendanceRecordItem> Records { get; } = [];
    public List<AttendanceMonthItem> Months { get; } = new();
    public bool FailCreate { get; set; }
    public bool FailLoad { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "GetAttendanceMonthsAsync":
                return FailLoad ? Task.FromException<IReadOnlyList<AttendanceMonthItem>>(new InvalidOperationException("Không tải được kỳ"))
                    : Task.FromResult<IReadOnlyList<AttendanceMonthItem>>(Months.ToArray());
            case "CreateAttendanceMonthAsync":
                if (FailCreate) return Task.FromException(new InvalidOperationException("Không lưu được kỳ"));
                Months.Add(new((int)args![0]!, (int)args[1]!, false));
                return Task.CompletedTask;
            case "DeleteAttendanceMonthAsync":
                var target = Months.Single(x => x.Year == (int)args![0]! && x.Month == (int)args[1]!);
                if (target.HasData) return Task.FromException(new InvalidOperationException("Kỳ có dữ liệu, không thể xóa"));
                Months.Remove(target);
                return Task.CompletedTask;
            case "SearchAsync": return Task.FromResult<IReadOnlyList<EmployeeListItem>>(Employees.ToArray());
            case "GetWorkShiftsAsync": return Task.FromResult<IReadOnlyList<WorkShiftItem>>(Shifts.ToArray());
            case "GetAttendanceRecordsAsync": return Task.FromResult<IReadOnlyList<AttendanceRecordItem>>(Records.ToArray());
            default: throw new NotSupportedException(method.Name);
        }
    }
}
