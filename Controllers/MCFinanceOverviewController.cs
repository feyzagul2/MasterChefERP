using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[Route("MCFinanceOverview")]
public sealed class MCFinanceOverviewController : Controller
{
    private readonly IConfiguration _configuration;

    public MCFinanceOverviewController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = new MCFinanceOverviewPage();

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            model.Warning = "Finans veritabanı bağlantısı bulunamadı.";
            return View(model);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var db =
                new SqliteConnection(builder.ToString());

            await db.OpenAsync();

            async Task<bool> HasTable(string name)
            {
                await using var command = db.CreateCommand();

                command.CommandText = @"
                    SELECT 1
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name = @name
                    LIMIT 1";

                command.Parameters.AddWithValue("@name", name);

                return await command.ExecuteScalarAsync() != null;
            }

            if (await HasTable("CariAccounts"))
            {
                try
                {
                    await using (var supplierCount = db.CreateCommand())
                    {
                        supplierCount.CommandText =
                            "SELECT COUNT(*) FROM CariAccounts WHERE IsSupplier = 1";

                        model.SupplierCount = Convert.ToInt32(
                            await supplierCount.ExecuteScalarAsync());
                    }

                    await using (var customerCount = db.CreateCommand())
                    {
                        customerCount.CommandText =
                            "SELECT COUNT(*) FROM CariAccounts WHERE IsCustomer = 1";

                        model.CustomerCount = Convert.ToInt32(
                            await customerCount.ExecuteScalarAsync());
                    }

                    await using (var suppliers = db.CreateCommand())
                    {
                        suppliers.CommandText = @"
                            SELECT CompanyName
                            FROM CariAccounts
                            WHERE IsSupplier = 1
                            ORDER BY CompanyName COLLATE NOCASE
                            LIMIT 6";

                        await using var reader =
                            await suppliers.ExecuteReaderAsync();

                        while (await reader.ReadAsync())
                        {
                            model.Suppliers.Add(
                                reader.IsDBNull(0)
                                    ? "-"
                                    : reader.GetString(0));
                        }
                    }

                    await using (var customers = db.CreateCommand())
                    {
                        customers.CommandText = @"
                            SELECT CompanyName
                            FROM CariAccounts
                            WHERE IsCustomer = 1
                            ORDER BY CompanyName COLLATE NOCASE
                            LIMIT 6";

                        await using var reader =
                            await customers.ExecuteReaderAsync();

                        while (await reader.ReadAsync())
                        {
                            model.Customers.Add(
                                reader.IsDBNull(0)
                                    ? "-"
                                    : reader.GetString(0));
                        }
                    }
                }
                catch (SqliteException)
                {
                    model.SupplierCount = null;
                    model.CustomerCount = null;
                    model.Suppliers.Clear();
                    model.Customers.Clear();

                    model.Warning =
                        "Cari hesap alanları kontrol gerektiriyor.";
                }
            }
            else
            {
                model.Warning = "Cari hesap tablosu bulunamadı.";
            }

            if (await HasTable("MCBankCashAccounts"))
            {
                try
                {
                    await using (var count = db.CreateCommand())
                    {
                        count.CommandText =
                            "SELECT COUNT(*) FROM MCBankCashAccounts";

                        model.BankAccountCount = Convert.ToInt32(
                            await count.ExecuteScalarAsync());
                    }

                    await using (var command = db.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT Name, Code
                            FROM MCBankCashAccounts
                            ORDER BY Name COLLATE NOCASE
                            LIMIT 8";

                        await using var reader =
                            await command.ExecuteReaderAsync();

                        while (await reader.ReadAsync())
                        {
                            var name = reader.IsDBNull(0)
                                ? "-"
                                : reader.GetString(0);

                            var code = reader.IsDBNull(1)
                                ? ""
                                : reader.GetString(1);

                            model.BankAccounts.Add(
                                string.IsNullOrWhiteSpace(code)
                                    ? name
                                    : name + " (" + code + ")");
                        }
                    }
                }
                catch (SqliteException)
                {
                    model.BankAccountCount = null;
                    model.BankAccounts.Clear();

                    model.Warning +=
                        " Banka hesap alanları kontrol gerektiriyor.";
                }
            }
            else
            {
                model.Warning += " Kasa ve banka tablosu bulunamadı.";
            }
        }
        catch (SqliteException)
        {
            model.Warning =
                "Finans verileri okunamadı. Kayıtlarda değişiklik yapılmadı.";
        }

        return View(model);
    }
}

public sealed class MCFinanceOverviewPage
{
    public int? SupplierCount { get; set; }
    public int? CustomerCount { get; set; }
    public int? BankAccountCount { get; set; }

    public string Warning { get; set; } = "";

    public List<string> Suppliers { get; set; } = new();
    public List<string> Customers { get; set; } = new();
    public List<string> BankAccounts { get; set; } = new();
}