using System.IO;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.UserManagement;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CustomerReportDatabaseTests
{
    [Fact]
    public async Task Top_report_maps_Firebird_count_and_filters_dates_status_company_and_limit()
    {
        var original = FirebirdDatabase.Profile;
        var path = Path.Combine(Path.GetTempPath(), $"kazuko-customer-report-{Guid.NewGuid():N}.fdb");
        var profile = original with { DatabasePath = path, UseEmbedded = true };
        try
        {
            var connectionString = FirebirdDatabase.BuildConnectionString(profile);
            FbConnection.CreateDatabase(connectionString);
            FirebirdDatabase.ApplyProfileForCurrentProcess(profile);
            await using (var connection = FirebirdDatabase.CreateConnection())
            {
                await connection.OpenAsync();
                foreach (var statement in new[]
                {
                    "CREATE TABLE COMPANIES (ID BIGINT, CODE VARCHAR(30))",
                    "CREATE TABLE ROLES (ID BIGINT, CODE VARCHAR(30), IS_ACTIVE BOOLEAN)",
                    "CREATE TABLE USER_GROUPS (ID BIGINT, COMPANY_ID BIGINT, IS_ACTIVE BOOLEAN)",
                    "CREATE TABLE USERS (ID BIGINT, COMPANY_ID BIGINT, ROLE_ID BIGINT, GROUP_ID BIGINT, IS_ACTIVE BOOLEAN)",
                    "CREATE TABLE CUSTOMER_GROUPS (ID BIGINT, NAME VARCHAR(100))",
                    "CREATE TABLE CUSTOMERS (ID BIGINT, COMPANY_ID BIGINT, CUSTOMER_GROUP_ID BIGINT, CODE VARCHAR(30), NAME VARCHAR(100), PHONE VARCHAR(30), IS_WALK_IN BOOLEAN DEFAULT FALSE NOT NULL)",
                    "CREATE TABLE SALES_ORDERS (ID BIGINT, CUSTOMER_ID BIGINT, ORDER_DATE TIMESTAMP, STATUS_CODE VARCHAR(30), GRAND_TOTAL DECIMAL(18,2))",
                    "INSERT INTO COMPANIES VALUES (1, 'KAZUKO')",
                    "INSERT INTO COMPANIES VALUES (2, 'OTHER')",
                    "INSERT INTO ROLES VALUES (1, 'ADMIN', TRUE)",
                    "INSERT INTO USERS VALUES (1, 1, 1, NULL, TRUE)",
                    "INSERT INTO CUSTOMER_GROUPS VALUES (1, 'VIP')",
                    "INSERT INTO CUSTOMERS VALUES (1, 1, 1, 'KH01', 'Customer A', '0901', FALSE)",
                    "INSERT INTO CUSTOMERS VALUES (2, 1, NULL, 'KH02', 'Customer B', NULL, FALSE)",
                    "INSERT INTO CUSTOMERS VALUES (3, 2, NULL, 'KH03', 'Other company', NULL, FALSE)",
                    "INSERT INTO CUSTOMERS VALUES (4, 1, NULL, 'ARBITRARY', 'Walk-in', NULL, TRUE)",
                    "INSERT INTO SALES_ORDERS VALUES (8, 4, TIMESTAMP '2026-10-15 00:00:00', 'COMPLETED', 5000)",
                    "INSERT INTO SALES_ORDERS VALUES (9, 4, TIMESTAMP '2026-10-19 23:59:59', 'COMPLETED', 500)",
                    "INSERT INTO SALES_ORDERS VALUES (10, 4, TIMESTAMP '2026-10-20 00:00:00', 'COMPLETED', 9000)",
                    "INSERT INTO SALES_ORDERS VALUES (11, 4, TIMESTAMP '2026-10-15 00:00:00', 'CANCELLED', 9000)",
                    "INSERT INTO SALES_ORDERS VALUES (1, 1, TIMESTAMP '2026-10-01 00:00:00', 'COMPLETED', 100)",
                    "INSERT INTO SALES_ORDERS VALUES (2, 1, TIMESTAMP '2026-10-19 23:59:59', 'COMPLETED', 200)",
                    "INSERT INTO SALES_ORDERS VALUES (3, 2, TIMESTAMP '2026-10-15 00:00:00', 'COMPLETED', 400)",
                    "INSERT INTO SALES_ORDERS VALUES (4, 1, TIMESTAMP '2026-10-20 00:00:00', 'COMPLETED', 1000)",
                    "INSERT INTO SALES_ORDERS VALUES (5, 1, TIMESTAMP '2026-10-15 00:00:00', 'DRAFT', 1000)",
                    "INSERT INTO SALES_ORDERS VALUES (6, 1, TIMESTAMP '2026-10-15 00:00:00', 'CANCELLED', 1000)",
                    "INSERT INTO SALES_ORDERS VALUES (7, 3, TIMESTAMP '2026-10-15 00:00:00', 'COMPLETED', 9000)"
                })
                    await connection.ExecuteAsync(statement);
            }
            var session = new CurrentSessionAccessor();
            session.Set(new(1, "admin", "Admin", "SALE", 1, "CN01", "Branch 1"));
            var authorization = new FirebirdAuthorizationService(session);
            await authorization.RefreshAsync();
            Assert.True(authorization.Current.IsSystemAdmin);
            var repo = AuthorizedServiceProxy<ICustomerRepository>.Create(new FirebirdCustomerRepository(), authorization, session);
            var rows = await repo.GetTopCustomersAsync(new(2026, 10, 1), new(2026, 10, 19), 10);
            var summary = await repo.GetSalesSummaryAsync(new(2026, 10, 1), new(2026, 10, 19));
            Assert.Equal(700m, Assert.Single(summary, x => !x.IsWalkIn).TotalSales);
            var walkIn = Assert.Single(summary, x => x.IsWalkIn);
            Assert.Equal(5500m, walkIn.TotalSales);
            Assert.Equal(2, walkIn.OrderCount);
            Assert.Equal(6200m, summary.Sum(x => x.TotalSales));
            Assert.Equal(new long[] { 2, 1 }, rows.Select(x => x.CustomerId));
            Assert.Equal(300, rows[1].TotalSales);
            Assert.Equal(2, rows[1].OrderCount);
            Assert.Equal("VIP", rows[1].CustomerGroupName);
            Assert.Equal("", rows[0].Phone);
            Assert.Single(await repo.GetTopCustomersAsync(new(2026, 10, 1), new(2026, 10, 19), 1));
        }
        finally
        {
            FbConnection.ClearAllPools();
            FirebirdDatabase.ApplyProfileForCurrentProcess(original);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
