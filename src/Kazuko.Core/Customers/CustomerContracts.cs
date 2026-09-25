using Kazuko.Core.Authentication;

namespace Kazuko.Core.Customers;

public sealed record CustomerListItem(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Email,
    string CustomerGroupName,
    string Region,
    decimal TotalSales,
    decimal CurrentDebt,
    DateTime? LastPurchaseDate,
    bool IsActive,
    string Address,
    string Gender,
    string Occupation,
    string MembershipRank,
    string TaxCode,
    string Notes,
    int OpeningPoints,
    decimal CreditLimit,
    DateTime? BirthOrEstablishedDate,
    DateTime? JoinDate,
    long CustomerGroupId);

public sealed record CustomerDetail(
    long Id,
    string Code,
    string Name,
    long CustomerGroupId,
    string CustomerGroupName,
    string Address,
    string Phone,
    string Email,
    DateTime? BirthOrEstablishedDate,
    int OpeningPoints,
    string TaxCode,
    string Notes,
    string Gender,
    string Region,
    string Occupation,
    string MembershipRank,
    DateTime? JoinDate,
    decimal CreditLimit,
    bool IsActive);

public sealed record CustomerGroupOption(long Id, string Code, string Name, bool IsActive, bool IsDefault)
{
    public override string ToString() => Name;
}

public sealed record CustomerSearchFilter(
    string SearchText,
    long GroupId,
    string Region,
    bool? IsActive);

public sealed record CustomerSaveRequest(
    long? Id,
    string Name,
    long CustomerGroupId,
    string Address,
    string Phone,
    string Email,
    DateTime? BirthOrEstablishedDate,
    int OpeningPoints,
    string TaxCode,
    string Notes,
    string Gender,
    string Region,
    string Occupation,
    string MembershipRank,
    DateTime? JoinDate,
    decimal CreditLimit,
    bool IsActive);

public sealed record CustomerEditRequest(long? CustomerId);

public sealed record CustomerEditResult(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Address,
    bool IsActive);

public interface ICustomerEditorService
{
    CustomerEditResult? ShowCreate();
    CustomerEditResult? ShowEdit(long customerId);
}

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        CustomerSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<CustomerDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<string> FindDuplicateWarningAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<CustomerEditResult> SaveAsync(
        CustomerSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
