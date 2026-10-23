using System.Text;
using Kazuko.Core.Customers;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CitizenIdentityScanTests
{
    internal const string Raw = "012345678901|123456789|NGUYỄN VĂN A|29022000|Nam|Đường Âu Cơ, TP.HCM|01012020";
    private static readonly DateTime Today = new(2026, 10, 22);

    [Theory]
    [InlineData(Raw)]
    [InlineData("012345678901||NGUYỄN VĂN A|29022000|Nam|Đường Âu Cơ, TP.HCM|01012020\r\n")]
    [InlineData("012345678901|NGUYỄN VĂN A|29022000|Nam|Đường Âu Cơ, TP.HCM|01012020")]
    [InlineData("012345678901|NGUYỄN VĂN A|29022000|Nam|Đường Âu Cơ, TP.HCM")]
    public void Parses_supported_shapes_and_preserves_leading_zero_and_vietnamese(string raw)
    {
        var parsed = CitizenIdentityQrParser.Parse(raw.Normalize(NormalizationForm.FormD), Today);
        Assert.True(parsed.Success, parsed.Error);
        Assert.Equal("012345678901", parsed.Data!.Number);
        Assert.Equal("NGUYỄN VĂN A", parsed.Data.FullName);
        Assert.Equal(new DateTime(2000, 2, 29), parsed.Data.BirthDate);
        Assert.Equal("Nam", parsed.Data.Gender);
    }

    [Theory]
    [InlineData("")]
    [InlineData("B600022")]
    [InlineData("https://example.com")]
    [InlineData("01234567890X||A|01012000|Nam|HCM|01012020")]
    [InlineData("012345678901|ABC|A|01012000|Nam|HCM|01012020")]
    [InlineData("012345678901|||01012000|Nam|HCM|01012020")]
    [InlineData("012345678901||A|29022001|Nam|HCM|01012020")]
    [InlineData("012345678901||A|01012030|Nam|HCM|01012020")]
    [InlineData("012345678901||A|01012000|INVALID|HCM|01012020")]
    [InlineData("012345678901||A|01012000|Nam|HCM|01011999")]
    [InlineData("012345678901||A|01012000|Nam|HCM|01012030")]
    [InlineData("012345678901||A|01012000|Nam|HCM|01012020|EXTRA")]
    [InlineData("012345678901||A\uFFFD|01012000|Nam|HCM|01012020")]
    [InlineData("012345678901||A\tB|01012000|Nam|HCM|01012020")]
    public void Rejects_invalid_input_without_partial_data(string raw)
    {
        var parsed = CitizenIdentityQrParser.Parse(raw, Today);
        Assert.False(parsed.Success);
        Assert.Null(parsed.Data);
        Assert.NotEmpty(parsed.Error);
    }

    [Fact]
    public void Rejects_oversized_data() => Assert.False(CitizenIdentityQrParser.Parse(new string('a', 4097), Today).Success);

    private static CitizenScanDialogViewModel Scanner(CustomerRepositoryFake repo, string currentNumber = "", bool canEdit = false) =>
        new(repo, CustomerTestContext.Session().Current!, null, new("Tên cũ", currentNumber, null, "", "Địa chỉ cũ", null), canEdit);

    [Fact]
    public async Task Scan_preview_requires_apply_and_invalid_rescan_clears_it()
    {
        var vm = Scanner(new()); vm.RawInput = Raw;
        await vm.ReadAsync();
        Assert.True(vm.HasData);
        Assert.Null(vm.Outcome);
        Assert.True(vm.ApplyCommand.CanExecute(null));
        Assert.Equal("", vm.RawInput);
        vm.RawInput = "INVALID";
        await vm.ReadAsync();
        Assert.Null(vm.Data);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Existing_customer_is_rechecked_and_inactive_or_system_matches_cannot_be_used()
    {
        var repo = new CustomerRepositoryFake { IdentityMatches = [new(42, "KH42", "Có sẵn", "0901", "HCM", "012345678901", true, false)] };
        var vm = Scanner(repo); vm.RawInput = Raw; await vm.ReadAsync();
        Assert.False(vm.ApplyCommand.CanExecute(null));
        repo.IdentityMatches = [repo.IdentityMatches[0] with { IsActive = false }];
        await vm.ChooseExistingAsync(false);
        Assert.Null(vm.Outcome); Assert.NotEmpty(vm.Error);
        repo.IdentityMatches = [repo.IdentityMatches[0] with { IsActive = true }];
        await vm.ChooseExistingAsync(false);
        Assert.Equal(42, vm.Outcome!.Existing!.Id);
        Assert.Empty(repo.Saved);
        var blocked = Scanner(new() { IdentityMatches = [repo.IdentityMatches[0] with { IsWalkIn = true }] });
        blocked.RawInput = Raw; await blocked.ReadAsync();
        Assert.False(blocked.UseExistingCommand.CanExecute(null));
        Assert.False(blocked.EditExistingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Different_identity_requires_confirmation_and_cancel_never_applies()
    {
        var vm = Scanner(new(), "999999999999"); vm.RawInput = Raw; await vm.ReadAsync();
        Assert.True(vm.RequiresIdentityConfirmation);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.ConfirmIdentityReplacement = true;
        Assert.True(vm.ApplyCommand.CanExecute(null));
        vm.Cancel();
        vm.ApplyCommand.Execute(null);
        Assert.Null(vm.Outcome);
    }

    [Fact]
    public async Task Closed_or_changed_scan_ignores_late_lookup()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<CustomerIdentityMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Scanner(new() { IdentityLookup = () => pending.Task });
        vm.RawInput = Raw; var work = vm.ReadAsync(); vm.Cancel(); pending.SetResult([]); await work;
        Assert.Null(vm.Outcome); Assert.Null(vm.Data);
        var second = new TaskCompletionSource<IReadOnlyList<CustomerIdentityMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm = Scanner(new() { IdentityLookup = () => second.Task }); vm.RawInput = Raw;
        work = vm.ReadAsync(); vm.RawInput = "INVALID"; second.SetResult([]); await work;
        Assert.Null(vm.Data);
    }

    [Fact]
    public async Task Applying_scan_preserves_phone_issue_place_and_business_fields_then_saves_old_id_hint()
    {
        var repo = new CustomerRepositoryFake();
        var vm = new CustomerEditDialogViewModel(repo, CustomerTestContext.Session(), null);
        await vm.Initialization;
        vm.Phone = "0901234567"; vm.IdIssuePlace = "Nơi cấp đã nhập"; vm.Notes = "Ghi chú"; vm.Email = "a@example.com";
        var data = CitizenIdentityQrParser.Parse(Raw, Today).Data!;
        Assert.True(vm.ApplyCitizenData(data));
        Assert.Equal("0901234567", vm.Phone); Assert.Equal("Nơi cấp đã nhập", vm.IdIssuePlace);
        Assert.Equal("Ghi chú", vm.Notes); Assert.Equal("a@example.com", vm.Email); Assert.Empty(repo.Saved);
        vm.SaveCommand.Execute(null);
        Assert.Equal("123456789", Assert.Single(repo.Saved).PreviousIdentityNumber);
        Assert.Equal("012345678901", repo.Saved[0].TaxCode);
        Assert.Equal(new DateTime(2020, 1, 1), repo.Saved[0].IdIssueDate);
    }

    [Fact]
    public async Task Save_blocks_existing_cccd_before_legacy_warning_and_existing_selection_does_not_save()
    {
        var match = new CustomerIdentityMatch(42, "KH42", "Existing", "0901", "HCM", "012345678901", true, false);
        var repo = new CustomerRepositoryFake { IdentityMatches = [match] };
        var vm = new CustomerEditDialogViewModel(repo, CustomerTestContext.Session(), null);
        await vm.Initialization; vm.ApplyCitizenData(CitizenIdentityQrParser.Parse(Raw, Today).Data!); vm.Phone = "0901";
        vm.SaveCommand.Execute(null);
        Assert.Empty(repo.Saved); Assert.Contains("đã có hồ sơ", vm.FormError);
        CustomerEditResult? selected = null; vm.RequestClose += result => selected = result;
        vm.CompleteWithExisting(match);
        Assert.Equal(42, selected!.Id); Assert.Empty(repo.Saved);
    }
    [Fact]
    public async Task Corrected_fields_are_applied_without_saving_or_changing_original_form_values()
    {
        var repo = new CustomerRepositoryFake();
        var vm = Scanner(repo); vm.RawInput = Raw; await vm.ReadAsync();
        vm.Changes[0].Scanned = "012345678902";
        vm.Changes[1].Scanned = "Đặng Thị Mỹ";
        vm.Changes[2].Scanned = "10/06/2004";
        vm.Changes[3].Scanned = "Nữ";
        vm.Changes[4].Scanned = "Đường Nguyễn Huệ, Đồng Nai";
        vm.Changes[5].Scanned = "03/06/2024";
        Assert.Equal("Tên cũ", vm.Changes[1].Current);
        vm.ApplyCommand.Execute(null);
        var data = vm.Outcome!.Data;
        Assert.Equal("012345678902", data.Number);
        Assert.Equal("Đặng Thị Mỹ", data.FullName);
        Assert.Equal(new DateTime(2004, 6, 10), data.BirthDate);
        Assert.Equal("Nữ", data.Gender);
        Assert.Equal("Đường Nguyễn Huệ, Đồng Nai", data.Address);
        Assert.Equal(new DateTime(2024, 6, 3), data.IssueDate);
        Assert.Empty(repo.Saved);
    }

    [Theory]
    [InlineData(0, "123")]
    [InlineData(1, "")]
    [InlineData(2, "31/02/2000")]
    [InlineData(3, "INVALID")]
    [InlineData(5, "01/01/1990")]
    [InlineData(5, "03/06/2099")]
    public async Task Invalid_corrections_cannot_be_applied(int field, string value)
    {
        var vm = Scanner(new()); vm.RawInput = Raw; await vm.ReadAsync();
        vm.Changes[field].Scanned = value;
        await vm.ApplyAsync();
        Assert.Null(vm.Outcome);
        Assert.NotEmpty(vm.Error);
        Assert.True(vm.HasData);
    }

    [Fact]
    public async Task Corrected_number_is_rechecked_and_matching_customer_must_be_selected()
    {
        var repo = new CustomerRepositoryFake();
        var vm = Scanner(repo); vm.RawInput = Raw; await vm.ReadAsync();
        string? checkedNumber = null;
        repo.IdentityLookup = () =>
        {
            checkedNumber = vm.Changes[0].Scanned;
            return Task.FromResult<IReadOnlyList<CustomerIdentityMatch>>([new(42, "KH42", "Có sẵn", "0901", "HCM", checkedNumber, true, false)]);
        };
        vm.Changes[0].Scanned = "012345678902";
        await vm.ApplyAsync();
        Assert.Equal("012345678902", checkedNumber);
        Assert.Null(vm.Outcome);
        Assert.True(vm.HasMatches);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        await vm.ChooseExistingAsync(false);
        Assert.Equal(42, vm.Outcome!.Existing!.Id);
        Assert.Equal("012345678902", vm.Outcome.Data.Number);
        Assert.Empty(repo.Saved);
    }

    [Fact]
    public async Task Changing_identity_resets_confirmation_and_late_apply_does_not_close_new_review()
    {
        var repo = new CustomerRepositoryFake();
        var vm = Scanner(repo, "999999999999"); vm.RawInput = Raw; await vm.ReadAsync();
        vm.ConfirmIdentityReplacement = true;
        vm.Changes[0].Scanned = "012345678902";
        Assert.False(vm.ConfirmIdentityReplacement);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        vm.ConfirmIdentityReplacement = true;
        var pending = new TaskCompletionSource<IReadOnlyList<CustomerIdentityMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.IdentityLookup = () => pending.Task;
        var apply = vm.ApplyAsync();
        vm.Changes[1].Scanned = "Tên sửa trong lúc chờ";
        pending.SetResult([]); await apply;
        Assert.Null(vm.Outcome);
        Assert.Equal("Tên sửa trong lúc chờ", vm.Changes[1].Scanned);
    }

}
