using System.IO;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Infrastructure.Company;
using Kazuko.Infrastructure.Data;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class CompanyLabelProfileTests
{
    [Fact]
    public async Task Parties_round_trip_separately_validate_lengths_and_isolate_company()
    {
        var original = FirebirdDatabase.Profile;
        string path = Path.Combine(Path.GetTempPath(), $"kazuko-label-profile-{Guid.NewGuid():N}.fdb");
        var profile = original with { DatabasePath = path, UseEmbedded = true };
        try
        {
            FbConnection.CreateDatabase(FirebirdDatabase.BuildConnectionString(profile));
            FirebirdDatabase.ApplyProfileForCurrentProcess(profile);
            await using (var connection = FirebirdDatabase.CreateConnection())
            {
                await connection.OpenAsync();
                foreach (string sql in new[] {
                    "CREATE TABLE COMPANIES (ID BIGINT PRIMARY KEY, NAME VARCHAR(200), PHONE VARCHAR(40), ADDRESS VARCHAR(500), LOGO_PATH VARCHAR(500), TCCS VARCHAR(100), DISTRIBUTOR_NAME VARCHAR(200), DISTRIBUTOR_ADDRESS VARCHAR(500), MANUFACTURER_NAME VARCHAR(200), MANUFACTURER_ADDRESS VARCHAR(500), COUNTRY_OF_ORIGIN VARCHAR(100), IS_ACTIVE BOOLEAN, UPDATED_AT TIMESTAMP)",
                    "CREATE TABLE BRANCHES (ID BIGINT PRIMARY KEY, COMPANY_ID BIGINT, IS_ACTIVE BOOLEAN)",
                    "INSERT INTO COMPANIES (ID,NAME,IS_ACTIVE) VALUES (1,'KAZUKO',TRUE)",
                    "INSERT INTO COMPANIES (ID,NAME,IS_ACTIVE) VALUES (2,'OTHER',TRUE)",
                    "INSERT INTO BRANCHES VALUES (1,1,TRUE)",
                    "INSERT INTO BRANCHES VALUES (2,2,TRUE)",
                    "INSERT INTO BRANCHES VALUES (3,1,FALSE)"
                })
                {
                    await using var command = new FbCommand(sql, connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
            var service = new FirebirdCompanyProfileService();
            var session = new AuthenticatedSession(1, "admin", "Admin", "ADMIN", 1, "CN", "Chi nhánh");
            var request = new CompanyProfileUpdateRequest("KAZUKO", null, "Công ty", null, "01:2026/KZJ",
                " NPP THÀNH ĐẠT ", " Điện Biên ", " NSX KAZUKO ", " Âu Cơ ", " Vietnam ");
            await service.SaveAsync(request, session);
            var actual = await service.GetAsync(session);
            Assert.Equal("NPP THÀNH ĐẠT", actual.DistributorName);
            Assert.Equal("Điện Biên", actual.DistributorAddress);
            Assert.Equal("NSX KAZUKO", actual.ManufacturerName);
            Assert.Equal("Âu Cơ", actual.ManufacturerAddress);
            Assert.Equal("Vietnam", actual.CountryOfOrigin);
            Assert.Equal("01:2026/KZJ", actual.FacilityStandard);
            Assert.Null((await service.GetAsync(session with { BranchId = 2 })).ManufacturerName);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(request with { ManufacturerName = new string('x', 201) }, session));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(request with { DistributorAddress = new string('x', 501) }, session));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(request with { CountryOfOrigin = new string('x', 101) }, session));
            Assert.Equal("NSX KAZUKO", (await service.GetAsync(session)).ManufacturerName);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(session with { BranchId = 3 }));
            await service.SaveAsync(request with { CountryOfOrigin = "  ", DistributorName = "" }, session);
            Assert.Null((await service.GetAsync(session)).CountryOfOrigin);
            Assert.Null((await service.GetAsync(session)).DistributorName);
        }
        finally
        {
            FirebirdDatabase.ApplyProfileForCurrentProcess(original);
            FbConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
