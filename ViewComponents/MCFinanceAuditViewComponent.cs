using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.ViewComponents;

public sealed class MCFinanceAuditViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;

    public MCFinanceAuditViewComponent(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new MCFinanceAuditModel();

        var cs = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
        {
            model.Warning = "Veritabanı bağlantısı bulunamadı.";
            return View(model);
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(cs)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var db = new SqliteConnection(builder.ToString());
            await db.OpenAsync();

            await using (var check = db.CreateCommand())
            {
                check.CommandText = @"
                    SELECT COUNT(*)
                    FROM sqlite_master
                    WHERE type='table'
                      AND name IN (
                        'CariAccounts',
                        'MCCariMovements',
                        'MCBankCashAccounts',
                        'MCBankCashMovements'
                      )";

                if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 4)
                {
                    model.Warning = "Finans tabloları eksik.";
                    return View(model);
                }
            }

            await using var cmd = db.CreateCommand();

            cmd.CommandText = @"
                SELECT
                    m.Id,
                    m.MovementType,
                    m.Debit,
                    m.Credit,
                    m.Currency,
                    m.RelatedBankMovementId,
                    c.CompanyName,
                    c.IsCustomer,
                    c.IsSupplier,
                    b.Direction,
                    b.Amount,
                    a.Currency,
                    b.Id,
                    b.AccountId,
                    b.RequestToken,
                    m.RequestToken
                FROM MCCariMovements m
                LEFT JOIN CariAccounts c
                    ON c.Id=m.CariAccountId
                LEFT JOIN MCBankCashMovements b
                    ON b.Id=m.RelatedBankMovementId
                LEFT JOIN MCBankCashAccounts a
                    ON a.Id=b.AccountId
                WHERE m.MovementType IN ('Collection','Payment')
                ORDER BY m.Id DESC";

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.CheckedCount++;

                var id = reader.GetInt64(0);
                var type = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var name = reader.IsDBNull(6)
                    ? "Cari hesap bulunamadı"
                    : reader.GetString(6);

                var issues = new List<string>();

                var debitValid = TryAmount(reader, 2, out var debit);
                var creditValid = TryAmount(reader, 3, out var credit);

                var expected = type == "Collection" ? credit : debit;

                if (!debitValid || !creditValid ||
                    expected <= 0 ||
                    (type == "Collection" && debit != 0) ||
                    (type == "Payment" && credit != 0))
                {
                    issues.Add("Cari tutarı geçersiz");
                }

                var customer = !reader.IsDBNull(7) &&
                    Convert.ToInt64(reader.GetValue(7)) != 0;

                var supplier = !reader.IsDBNull(8) &&
                    Convert.ToInt64(reader.GetValue(8)) != 0;

                if (type == "Collection" && !customer)
                    issues.Add("Tahsilat müşteri hesabına bağlı değil");

                if (type == "Payment" && !supplier)
                    issues.Add("Ödeme tedarikçi hesabına bağlı değil");

                if (reader.IsDBNull(5))
                {
                    issues.Add("Bağlı banka hareketi yok");
                }
                else if (reader.IsDBNull(12) || reader.IsDBNull(13))
                {
                    issues.Add("Banka hareketi veya hesabı bulunamadı");
                }
                else
                {
                    var direction = reader.IsDBNull(9)
                        ? ""
                        : reader.GetString(9);

                    var expectedDirection =
                        type == "Collection" ? "In" : "Out";

                    if (direction != expectedDirection)
                        issues.Add("Banka giriş/çıkış yönü uyuşmuyor");

                    if (!TryAmount(reader, 10, out var bankAmount) ||
                        bankAmount != expected)
                        issues.Add("Banka ve cari tutarı uyuşmuyor");

                    var cariCurrency = reader.IsDBNull(4)
                        ? ""
                        : reader.GetString(4).Trim();

                    var bankCurrency = reader.IsDBNull(11)
                        ? ""
                        : reader.GetString(11).Trim();

                    if (string.IsNullOrWhiteSpace(cariCurrency) ||
                        !string.Equals(
                            cariCurrency,
                            bankCurrency,
                            StringComparison.OrdinalIgnoreCase))
                        issues.Add("Para birimi uyuşmuyor");

                    var bankToken = reader.IsDBNull(14)
                        ? ""
                        : reader.GetString(14);

                    var cariToken = reader.IsDBNull(15)
                        ? ""
                        : reader.GetString(15);

                    if (string.IsNullOrWhiteSpace(bankToken) ||
                        !string.Equals(
                            bankToken,
                            cariToken,
                            StringComparison.Ordinal))
                        issues.Add("İşlem kimliği uyuşmuyor");
                }

                if (issues.Count == 0)
                {
                    model.ValidCount++;
                }
                else
                {
                    model.IssueCount++;

                    if (model.Issues.Count < 30)
                    {
                        model.Issues.Add(new MCFinanceAuditIssue
                        {
                            Id = id,
                            Company = name,
                            Type = type,
                            Message = string.Join("; ", issues)
                        });
                    }
                }
            }
        }
        catch (Exception ex) when (
            ex is SqliteException ||
            ex is InvalidOperationException ||
            ex is FormatException ||
            ex is OverflowException)
        {
            model.Warning = "Finans hareketleri denetlenemedi.";
            model.Issues.Clear();
            model.CheckedCount = 0;
            model.ValidCount = 0;
            model.IssueCount = 0;
        }

        return View(model);
    }

    private static bool TryAmount(
        SqliteDataReader reader,
        int index,
        out decimal amount)
    {
        amount = 0;

        if (reader.IsDBNull(index))
            return false;

        var value = Convert.ToString(
            reader.GetValue(index),
            CultureInfo.InvariantCulture);

        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out amount);
    }
}

public sealed class MCFinanceAuditModel
{
    public string Warning { get; set; } = "";
    public int CheckedCount { get; set; }
    public int ValidCount { get; set; }
    public int IssueCount { get; set; }
    public List<MCFinanceAuditIssue> Issues { get; set; } = new();
}

public sealed class MCFinanceAuditIssue
{
    public long Id { get; set; }
    public string Company { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}