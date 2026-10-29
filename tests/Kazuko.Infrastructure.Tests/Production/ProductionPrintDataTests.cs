using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Production;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Production;

[Collection(nameof(FirebirdTestCollection))]
public sealed class ProductionPrintDataTests
{
    [Fact]
    public async Task Order_print_reads_planned_line_fields_and_keeps_line_order_and_scope()
    {
        var original = FirebirdDatabase.Profile;
        var path = Path.Combine(Path.GetTempPath(), $"kazuko-production-print-{Guid.NewGuid():N}.fdb");
        var profile = original with { DatabasePath = path, UseEmbedded = true };
        try
        {
            FbConnection.CreateDatabase(FirebirdDatabase.BuildConnectionString(profile));
            FirebirdDatabase.ApplyProfileForCurrentProcess(profile);
            await using (var connection = FirebirdDatabase.CreateConnection())
            {
                await connection.OpenAsync();
                foreach (var sql in new[]
                {
                    "CREATE TABLE BRANCHES (ID BIGINT, NAME VARCHAR(100))",
                    "CREATE TABLE CUSTOMERS (ID BIGINT, COMPANY_ID BIGINT, NAME VARCHAR(100))",
                    "CREATE TABLE SALES_ORDERS (ID BIGINT, CUSTOMER_ID BIGINT, CODE VARCHAR(40))",
                    "CREATE TABLE EXTERNAL_WORKERS (ID BIGINT, COMPANY_ID BIGINT, NAME VARCHAR(100))",
                    "CREATE TABLE HR_EMPLOYEES (ID BIGINT, FULL_NAME VARCHAR(100))",
                    "CREATE TABLE PRODUCTION_ORDER_WORKERS (PRODUCTION_ORDER_ID BIGINT, EMPLOYEE_ID BIGINT)",
                    "CREATE TABLE STOCK_DOCUMENTS (ID BIGINT, DOCUMENT_DATE DATE)",
                    "CREATE TABLE PRODUCTION_ORDERS (ID BIGINT, COMPANY_ID BIGINT, BRANCH_ID BIGINT, CODE VARCHAR(40), ORDER_DATE DATE, DUE_DATE DATE, STATUS_CODE VARCHAR(30), EXECUTION_TYPE VARCHAR(30), EXTERNAL_WORKER_ID BIGINT, SALES_ORDER_ID BIGINT, MATERIAL_ISSUE_DOCUMENT_ID BIGINT, NOTES VARCHAR(500))",
                    // Field names from migrations 45/59; deliberately no obsolete print-only columns.
                    "CREATE TABLE PRODUCTION_ORDER_LINES (PRODUCTION_ORDER_ID BIGINT, LINE_NUMBER INTEGER, MODEL_NAME VARCHAR(200), PLANNED_QUANTITY INTEGER, ESTIMATED_WEIGHT_CHI DECIMAL(18,4), GOLD_TYPE_CODE VARCHAR(30), LABOR_COST_PER_UNIT DECIMAL(18,2))",
                    "INSERT INTO BRANCHES VALUES (1, 'Branch')",
                    "INSERT INTO PRODUCTION_ORDERS (ID, COMPANY_ID, BRANCH_ID, CODE, ORDER_DATE, STATUS_CODE, EXECUTION_TYPE) VALUES (1, 1, 1, 'SX-TEST', DATE '2026-10-29', 'DRAFT', 'INTERNAL')",
                    "INSERT INTO PRODUCTION_ORDER_LINES VALUES (1, 2, 'Ring B', 3, 1.2345, '9999', 125000)",
                    "INSERT INTO PRODUCTION_ORDER_LINES VALUES (1, 1, 'Ring A', 1, 0, '750', 0)",
                    "INSERT INTO PRODUCTION_ORDER_LINES VALUES (2, 1, 'Other order', 9, 9, '9999', 999999)"
                })
                    await connection.ExecuteAsync(sql);
            }

            var service = new FirebirdProductionPrintDataService();
            var dto = await service.GetOrderPrintAsync(1);
            Assert.NotNull(dto);
            Assert.Equal("SX-TEST", dto.OrderCode);
            Assert.Equal(2, dto.Lines.Count);
            Assert.Equal(1, dto.Lines[0].LineNumber);
            Assert.Equal(0m, dto.Lines[0].GoldWeight);
            Assert.Equal(0m, dto.Lines[0].LaborCost);
            var line = dto.Lines[1];
            Assert.Equal("Ring B", line.ModelName);
            Assert.Equal(3, line.PlannedQuantity);
            Assert.Equal(1.2345m, line.GoldWeight);
            Assert.Equal("9999", line.GoldPurityCode);
            Assert.Equal(125000m, line.LaborCost);
            Assert.Null(await service.GetOrderPrintAsync(999));
        }
        finally
        {
            FirebirdDatabase.ApplyProfileForCurrentProcess(original);
            FbConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
