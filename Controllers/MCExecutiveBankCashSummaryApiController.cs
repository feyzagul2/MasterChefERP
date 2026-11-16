using MasterChefTakip.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.FinanceManage)]
[ApiController]
[Route("api/mc-executive-bankcash-summary")]
public sealed class MCExecutiveBankCashSummaryApiController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public MCExecutiveBankCashSummaryApiController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return StatusCode(500);
        var b = new SqliteConnectionStringBuilder(cs) { Mode = SqliteOpenMode.ReadOnly };
        await using var db = new SqliteConnection(b.ToString());
        await db.OpenAsync();

        var movementCount = await ScalarLong(db, "SELECT COUNT(*) FROM MCBankCashMovements");
        var invalidCount = await ScalarLong(db, "SELECT COUNT(*) FROM MCBankCashMovements WHERE Amount < 0 OR Direction NOT IN ('In','Out')");

        var accounts = new List<object>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT a.Id,a.Code,a.Name,a.AccountType,a.Currency,COUNT(m.Id),SUM(CASE WHEN m.Direction='In' THEN CAST(m.Amount AS REAL) WHEN m.Direction='Out' THEN -CAST(m.Amount AS REAL) ELSE 0 END) FROM MCBankCashAccounts a LEFT JOIN MCBankCashMovements m ON m.AccountId=a.Id WHERE a.IsActive=1 GROUP BY a.Id,a.Code,a.Name,a.AccountType,a.Currency ORDER BY a.AccountType,a.Name";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var count = r.GetInt64(5);
                accounts.Add(new { id=r.GetInt64(0), code=r.IsDBNull(1)?"":r.GetString(1), name=r.IsDBNull(2)?"":r.GetString(2), accountType=r.IsDBNull(3)?"":r.GetString(3), currency=r.IsDBNull(4)?"":r.GetString(4), movementCount=count, balance=count>0 && !r.IsDBNull(6) ? Convert.ToDecimal(r.GetValue(6)) : (decimal?)null });
            }
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        decimal externalIn=0,externalOut=0,transferIn=0,transferOut=0;
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT Direction,Amount,COALESCE(ReferenceNumber,''),COALESCE(RequestToken,'') FROM MCBankCashMovements WHERE date(MovementDate)=date(@d)";
            cmd.Parameters.AddWithValue("@d",today);
            await using var r = await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync())
            {
                var direction=r.GetString(0);
                var amount=Convert.ToDecimal(r.GetValue(1));
                var reference=r.GetString(2);
                var token=r.GetString(3);
                var transfer=reference.StartsWith("VIR-",StringComparison.OrdinalIgnoreCase) && (token.EndsWith(":IN",StringComparison.OrdinalIgnoreCase) || token.EndsWith(":OUT",StringComparison.OrdinalIgnoreCase));
                if(transfer){if(direction=="In")transferIn+=amount;else if(direction=="Out")transferOut+=amount;}
                else{if(direction=="In")externalIn+=amount;else if(direction=="Out")externalOut+=amount;}
            }
        }

        var transferIssues = await ScalarLong(db, @"SELECT COUNT(*) FROM (SELECT ReferenceNumber,SUM(CASE WHEN Direction='In' THEN CAST(Amount AS REAL) ELSE 0 END) i,SUM(CASE WHEN Direction='Out' THEN CAST(Amount AS REAL) ELSE 0 END) o,SUM(CASE WHEN Direction='In' THEN 1 ELSE 0 END) ic,SUM(CASE WHEN Direction='Out' THEN 1 ELSE 0 END) oc FROM MCBankCashMovements WHERE ReferenceNumber LIKE 'VIR-%' GROUP BY ReferenceNumber HAVING ic<>1 OR oc<>1 OR ABS(i-o)>0.0001)");

        var recent = new List<object>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.MovementDate,m.Direction,m.Amount,COALESCE(m.Description,''),COALESCE(m.ReferenceNumber,''),a.Name,a.Code,a.Currency FROM MCBankCashMovements m INNER JOIN MCBankCashAccounts a ON a.Id=m.AccountId ORDER BY m.MovementDate DESC,m.Id DESC LIMIT 8";
            await using var r = await cmd.ExecuteReaderAsync();
            while(await r.ReadAsync()) recent.Add(new { date=r.GetString(0), direction=r.GetString(1), amount=Convert.ToDecimal(r.GetValue(2)), description=r.GetString(3), reference=r.GetString(4), account=r.GetString(5), code=r.GetString(6), currency=r.GetString(7) });
        }

        return Ok(new { hasData=movementCount>0, movementCount, invalidCount, externalIn, externalOut, transferIn, transferOut, transferIssues, accounts, recent });
    }

    private static async Task<long> ScalarLong(SqliteConnection db,string sql)
    {
        await using var cmd=db.CreateCommand();
        cmd.CommandText=sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
