using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using FastReport;
using FastReport.Export.PdfSimple;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Reports;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Reports.Shared;
using Kazuko.Shared.UI.Services;
using Microsoft.Win32;

namespace Kazuko.Modules.Reports;

public sealed class PayrollReportViewModel : ReportWorkspaceViewModel, IDisposable
{
    private readonly IPayrollReportService _service;
    private readonly IAuthorizationService? _permissions;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly PayrollReportPrintService _printer = new();
    private PayrollReportResult? _result;
    private bool _disposed, _draft, _updatingEmployees;
    private int _filterVersion;
    private string _year = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
    private int _month = DateTime.Today.Month;
    private PayrollReportEmployee? _employee;
    public PayrollReportViewModel(IPayrollReportService service, ICurrentSessionAccessor session, bool individual = false, IAuthorizationService? permissions = null)
        : base(session,individual?"hr-payroll-slip":"hr-payroll-summary",individual?null:PayrollReportColumns.SettingsKey)
    {
        _service=service;_permissions=permissions;IsIndividual=individual;
        Employees.Add(AllEmployee);_employee=Employees[0];
        SavePdfCommand=new AsyncRelayCommand(SavePdfAsync,()=>CanOutput);
        ExportExcelCommand=new AsyncRelayCommand(ExportExcelAsync,()=>CanOutput);
        PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(IsLoading)){OnPropertyChanged(nameof(CanFilter));NotifyOutputs();}};
        if(permissions is not null)WeakEventManager<IAuthorizationService,EventArgs>.AddHandler(permissions,nameof(IAuthorizationService.Changed),PermissionsChanged);
    }
    public bool IsIndividual {get;}
    public string Title=>IsIndividual?"Phiếu lương cá nhân":"Bảng lương tháng";
    public string Instructions=>IsIndividual?"Chọn kỳ và một nhân viên để xem phiếu lương, diễn giải và ký nhận.":"Số liệu từ bảng lương đã lưu. Đã chi chỉ gồm phiếu chi lương đang ghi sổ.";
    public IReadOnlyList<int> Months {get;}=Enumerable.Range(1,12).ToArray();
    public ObservableCollection<PayrollReportEmployee> Employees {get;}=[];
    public ObservableCollection<PayrollReportRow> Rows {get;}=[];
    private PayrollReportEmployee AllEmployee=>new(0,"",IsIndividual?"Chọn nhân viên…":"Tất cả nhân viên");
    public string YearText {get=>_year;set {if(SetProperty(ref _year,value))Changed(true);}}
    public int Month {get=>_month;set {if(SetProperty(ref _month,value))Changed(true);}}
    public bool IncludeDraft {get=>_draft;set {if(SetProperty(ref _draft,value))Changed(true);}}
    public PayrollReportEmployee? SelectedEmployee {get=>_employee;set {if(SetProperty(ref _employee,value) && !_updatingEmployees)Changed(false);}}
    public bool CanFilter=>!IsLoading;
    public AsyncRelayCommand SavePdfCommand {get;}
    public AsyncRelayCommand ExportExcelCommand {get;}
    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns=>PayrollReportColumns.All;
    protected override bool HasPrintableData=>!_disposed && Allowed && _result is {Rows.Count:>0} && (!IsIndividual || _result.Filter.EmployeeId is >0);
    private bool CanOutput=>!IsLoading && HasPrintableData && Allowed;
    private bool Allowed=>_permissions is null || (_permissions.Current.Allows("REP_VIEW") && _permissions.Current.Allows("HR_PAYROLL"));
    protected override string EmptyResultMessage=>IsIndividual && SelectedEmployee?.Id is not >0 ? "Chọn nhân viên trong kỳ để xem phiếu lương." : IncludeDraft?"Kỳ này chưa có bảng lương hoặc không có nhân viên phù hợp.":"Kỳ này chưa có bảng lương đã chốt. Có thể bật Cho phép bản nháp để kiểm tra.";
    private int Year=>int.TryParse(YearText,out var year)&&year is >=1900 and <=9998?year:throw new InvalidOperationException("Năm phải từ 1900 đến 9998.");
    private void Changed(bool resetEmployees)
    {
        _filterVersion++;
        _result=null;Rows.Clear();IsEmpty=true;ClearPreviewPdf();
        if(resetEmployees)ReplaceEmployees([],null);
        NotifyOutputs();ScheduleReload(250);
    }
    protected override void ResetFiltersCore()
    {
        _filterVersion++;
        _year=DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);_month=DateTime.Today.Month;_draft=false;
        ReplaceEmployees([],null);
        OnPropertyChanged(nameof(YearText));OnPropertyChanged(nameof(Month));OnPropertyChanged(nameof(IncludeDraft));OnPropertyChanged(nameof(SelectedEmployee));
        _result=null;Rows.Clear();
    }
    protected override async Task LoadDataAsync(AuthenticatedSession session,CancellationToken token)
    {
        if(_disposed)throw new OperationCanceledException();
        var version=_filterVersion;
        var year=Year;var month=Month;var draft=IncludeDraft;var employeeId=SelectedEmployee?.Id is >0?SelectedEmployee.Id:(long?)null;
        var people=await _service.GetEmployeesAsync(year,month,draft,session,token);token.ThrowIfCancellationRequested();
        if(version!=_filterVersion)throw new OperationCanceledException();
        if(!Allowed)throw new UnauthorizedAccessException("Không có quyền xem báo cáo lương.");
        if(_disposed)throw new OperationCanceledException();
        ReplaceEmployees(people,employeeId);
        employeeId=SelectedEmployee?.Id is >0?SelectedEmployee.Id:null;
        if(IsIndividual && employeeId is null){_result=null;Rows.Clear();IsEmpty=true;SummaryText="Chọn nhân viên trong kỳ để xem phiếu.";NotifyOutputs();return;}
        var result=await _service.GetAsync(new(year,month,employeeId,draft),session,token);token.ThrowIfCancellationRequested();
        if(version!=_filterVersion)throw new OperationCanceledException();
        if(!Allowed)throw new UnauthorizedAccessException("Không có quyền xem báo cáo lương.");
        if(_disposed)throw new OperationCanceledException();
        _result=result;Rows.Clear();foreach(var row in result.Rows)Rows.Add(row);
        IsEmpty=Rows.Count==0;
        SummaryText=IsEmpty?$"Không có dữ liệu kỳ {month:00}/{year} theo bộ lọc.":$"{(result.IsDraft?"BẢN NHÁP":"Đã chốt")} · {month:00}/{year} · {Rows.Count} nhân viên · Thực nhận {result.Rows.Sum(x=>x.NetSalary):N0} · Đã chi {result.Rows.Sum(x=>x.PaidAmount):N0} · Còn phải chi {result.Rows.Sum(x=>x.RemainingAmount):N0} VND";
        NotifyOutputs();
    }
    private void ReplaceEmployees(IEnumerable<PayrollReportEmployee> people,long? selectedId)
    {
        // A bound ComboBox writes null to SelectedEmployee during Clear(). This is an
        // internal refresh, so it must not invalidate/cancel the report being loaded.
        _updatingEmployees=true;
        try
        {
            Employees.Clear();Employees.Add(AllEmployee);
            foreach(var person in people)Employees.Add(person);
            SelectedEmployee=Employees.FirstOrDefault(x=>x.Id==selectedId)??Employees[0];
        }
        finally { _updatingEmployees=false; }
    }
    protected override Report CreatePreparedReport(AuthenticatedSession session)
    {
        _permissions?.DemandAsync([new("REP_VIEW"),new("HR_PAYROLL")]).GetAwaiter().GetResult();
        var data=_result??throw new InvalidOperationException("Tải lại báo cáo trước khi in.");
        return IsIndividual?_printer.CreateIndividual(data,session):_printer.CreateSummary(data,session,ActiveColumns);
    }
    protected override void OnLoadFailed(){_result=null;Rows.Clear();NotifyOutputs();}
    public static IReadOnlyList<ExcelExportColumn<PayrollReportRow>> ExcelColumns {get;}=
    [
        new("Code","Mã NV",x=>x.Code),new("Name","Nhân viên",x=>x.Name),new("MethodLabel","Cách tính",x=>x.MethodLabel),
        new("StandardUnits","Công chuẩn",x=>x.StandardUnits,ExcelCellKind.Quantity),new("ActualUnits","Công tính lương",x=>x.ActualUnits,ExcelCellKind.Quantity),
        new("Rate","Đơn giá ca/tháng",x=>x.Rate,ExcelCellKind.Money),new("Salary","Lương tính được",x=>x.Salary,ExcelCellKind.Money),
        new("RecordedBonus","Thưởng ghi nhận",x=>x.RecordedBonus,ExcelCellKind.Money),new("ManualBonus","Thưởng thêm",x=>x.ManualBonus,ExcelCellKind.Money),new("ManualBonusNote","Diễn giải thưởng thêm",x=>x.ManualBonusNote),
        new("RecordedDeduction","Khấu trừ ghi nhận",x=>x.RecordedDeduction,ExcelCellKind.Money),new("ManualDeduction","Khấu trừ thêm",x=>x.ManualDeduction,ExcelCellKind.Money),new("ManualDeductionNote","Diễn giải khấu trừ thêm",x=>x.ManualDeductionNote),
        new("RecordedNotes","Nội dung ghi nhận",x=>x.RecordedNotes),new("NetSalary","Thực nhận",x=>x.NetSalary,ExcelCellKind.Money),new("PaidAmount","Đã chi",x=>x.PaidAmount,ExcelCellKind.Money),
        new("RemainingAmount","Còn phải chi",x=>x.RemainingAmount,ExcelCellKind.Money),new("PaymentStatus","Thanh toán",x=>x.PaymentStatus),new("VoucherCode","Phiếu chi",x=>x.VoucherCode),new("PaymentDate","Ngày chi",x=>x.PaymentDate,ExcelCellKind.Date)
    ];
    private async Task SavePdfAsync()
    {
        try
        {
            if(_permissions is not null)await _permissions.DemandAsync([new("REP_VIEW"),new("HR_PAYROLL"),new("REP_EXPORT")]);
            if(!CanOutput || _result is null)throw new InvalidOperationException("Tải lại báo cáo trước khi lưu PDF.");
            var dialog=new SaveFileDialog{Title="Lưu báo cáo PDF",Filter="PDF (*.pdf)|*.pdf",DefaultExt=".pdf",AddExtension=true,FileName=$"{(IsIndividual?"PhieuLuong":"BangLuong")}_{_result.Filter.Year}_{_result.Filter.Month:00}{(_result.IsDraft?"_BAN_NHAP":"")}.pdf"};
            if(dialog.ShowDialog()!=true)return;
            using var report=CreatePreparedReport(SessionAccessor.Current??throw new InvalidOperationException("Vui lòng đăng nhập lại."));
            using var export=new PDFSimpleExport();report.Export(export,dialog.FileName);StatusMessage="Đã lưu PDF.";
        }
        catch(Exception ex){ErrorMessage=ex.Message;}
    }
    private async Task ExportExcelAsync()
    {
        try
        {
            if(_permissions is not null)await _permissions.DemandAsync([new("REP_VIEW"),new("HR_PAYROLL"),new("DATA_EXPORT")]);
            if(!CanOutput || _result is null)throw new InvalidOperationException("Tải lại báo cáo trước khi xuất Excel.");
            var data=_result;
            ExcelExportColumn<PayrollReportRow>[] columns=[
                new("Period","Kỳ lương",_=>$"{data.Filter.Month:00}/{data.Filter.Year}"),
                new("PeriodStatus","Trạng thái kỳ",_=>data.IsDraft?"BẢN NHÁP - CHƯA CHỐT":"ĐÃ CHỐT"),..ExcelColumns];
            ExcelExportService.Export("Xuất báo cáo lương",$"BangLuong_{data.Filter.Year}_{data.Filter.Month:00}{(data.IsDraft?"_BAN_NHAP":"_DaChot")}",null,data.Rows,columns);
        }
        catch(Exception ex){ErrorMessage=ex.Message;}
    }
    private void NotifyOutputs()
    {
        SavePdfCommand?.NotifyCanExecuteChanged();ExportExcelCommand?.NotifyCanExecuteChanged();
        (PreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();(PrintCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
    private void PermissionsChanged(object? sender,EventArgs e)
    {
        void Apply(){if(Allowed || _disposed)return;_result=null;Rows.Clear();IsEmpty=true;ClearPreviewPdf();ErrorMessage="Quyền xem báo cáo lương đã thay đổi.";NotifyOutputs();}
        if(_dispatcher.CheckAccess())Apply();else _dispatcher.BeginInvoke(new Action(Apply));
    }
    public void Dispose(){_disposed=true;if(_permissions is not null)WeakEventManager<IAuthorizationService,EventArgs>.RemoveHandler(_permissions,nameof(IAuthorizationService.Changed),PermissionsChanged);_result=null;Rows.Clear();ClearPreviewPdf();}
}
