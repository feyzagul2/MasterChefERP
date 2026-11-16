using System;



using MasterChefTakip.Data;



using MasterChefTakip.Models;



using Microsoft.AspNetCore.Authorization;



using Microsoft.AspNetCore.Mvc;



using Microsoft.Data.Sqlite;



using Microsoft.EntityFrameworkCore;







namespace MasterChefTakip.Controllers;







// MC_INVENTORY_CARDS_V1



// HM disindaki malzemeler, hammadde hareketlerinden ayri tutulur.







[Authorize(Roles = AppRoles.Admin)]



public class MCInventoryCardsController : Controller



{



    private readonly ApplicationDbContext _db;







    public MCInventoryCardsController(ApplicationDbContext db)



    {



        _db = db;



    }







    [HttpGet]



    public async Task<IActionResult> Create(int catalogId)



    {



        if (catalogId <= 0)



            return BadRequest();







        await using var db = Open();



        await db.OpenAsync();







        var model = await FindMaterial(db, catalogId);







        if (model == null)



            return NotFound();







        await PopulateExistingCard(db, model);


        model.CatalogFields = await LoadCatalogFields(db, catalogId);
        return View(model);



    }







    [HttpPost]



    [ValidateAntiForgeryToken]



    public async Task<IActionResult> Create(



        int catalogId,



        string? unit,



        bool confirmed)



    {



        if (catalogId <= 0)



            return BadRequest();







        unit = (unit ?? "").Trim().ToUpperInvariant();







        string[] allowed =



        {



            "KG", "L", "ADET", "GR", "ML",



            "TON", "METRE", "M2", "M3"



        };







        await using var db = Open();



        await db.OpenAsync();







        var model = await FindMaterial(db, catalogId);







        if (model == null)



            return NotFound();







        // MC_EXISTING_RAW_CARD_GUARD_V2



        // Daha once hammadde stok kartina baglanmis malzemeye



        // ikinci bir envanter karti olusturulamaz.



        await using (var check = db.CreateCommand())



        {



            check.CommandText = @"



                SELECT COUNT(*)



                FROM MCMaterialStockCardLinks



                WHERE MaterialCatalogId = @id";







            check.Parameters.AddWithValue("@id", catalogId);







            if (Convert.ToInt64(await check.ExecuteScalarAsync()) > 0)



            {



                model.Error =



                    "Bu malzeme zaten bir hammadde stok kartina bagli.";



                return View(model);



            }



        }







        model.Unit = unit;







        if (!confirmed || !Array.Exists(allowed, x => x == unit))



        {



            model.Error = "Gercek olcu birimini secip bilgileri onaylayiniz.";



            return View(model);



        }







        if (model.Code.Length == 0 ||



            model.Code.Length > 120 ||



            model.Name.Length == 0 ||



            model.Name.Length > 300 ||



            model.Type.Length == 0 ||



            model.Type.Length > 100)



        {



            return BadRequest("Katalog kaydi stok karti alanlarina uygun degil.");



        }







        


        await PopulateExistingCard(db, model);





        if (model.HasExistingCard)


        {


            model.Error = "Bu malzemenin stok karti zaten mevcut.";


            return View(model);


        }





        await using var transaction = (SqliteTransaction)



            await db.BeginTransactionAsync();







        try



        {



            await using (var schema = db.CreateCommand())



            {



                schema.Transaction = transaction;



                schema.CommandText = @"



                    CREATE TABLE IF NOT EXISTS MCInventoryCards



                    (



                        Id INTEGER PRIMARY KEY AUTOINCREMENT,



                        CatalogId INTEGER NOT NULL UNIQUE,



                        MaterialCode TEXT NOT NULL,



                        MaterialName TEXT NOT NULL,



                        MaterialType TEXT NOT NULL,



                        Unit TEXT NOT NULL,



                        IsActive INTEGER NOT NULL DEFAULT 1,



                        CreatedAtUtc TEXT NOT NULL,



                        FOREIGN KEY (CatalogId)



                            REFERENCES MCMaterialCatalog(Id)



                    )";







                await schema.ExecuteNonQueryAsync();



            }







            await using (var insert = db.CreateCommand())



            {



                insert.Transaction = transaction;



                insert.CommandText = @"



                    INSERT INTO MCInventoryCards



                    (



                        CatalogId,



                        MaterialCode,



                        MaterialName,



                        MaterialType,



                        Unit,



                        IsActive,



                        CreatedAtUtc



                    )



                    VALUES



                    (



                        @id,



                        @code,



                        @name,



                        @type,



                        @unit,



                        1,



                        @date



                    )";







                insert.Parameters.AddWithValue("@id", model.CatalogId);



                insert.Parameters.AddWithValue("@code", model.Code);



                insert.Parameters.AddWithValue("@name", model.Name);



                insert.Parameters.AddWithValue("@type", model.Type);



                insert.Parameters.AddWithValue("@unit", unit);



                insert.Parameters.AddWithValue(



                    "@date", DateTime.UtcNow.ToString("O"));







                await insert.ExecuteNonQueryAsync();



            }







            await transaction.CommitAsync();







            TempData["MCInventorySuccess"] =



                "Malzeme stok karti olusturuldu.";







            return RedirectToAction(



                nameof(Create),



                new { catalogId });



        }



        catch (SqliteException)



        {



            await transaction.RollbackAsync();







            model.Error =



                "Kayit olusturulamadi. Bu malzemenin karti mevcut olabilir.";







            return View(model);



        }



    }










    // MC_EXISTING_CARD_CHECK_V4


    private static async Task PopulateExistingCard(


        SqliteConnection db,


        MCInventoryCardInput model)


    {


        await using var cmd = db.CreateCommand();





        cmd.CommandText = @"


            SELECT COUNT(*)


            FROM sqlite_master


            WHERE type='table' AND name='MCInventoryCards'";





        var tableExists = Convert.ToInt64(


            await cmd.ExecuteScalarAsync() ?? 0L) > 0;





        if (tableExists)


        {


            cmd.CommandText = @"


                SELECT Unit


                FROM MCInventoryCards


                WHERE CatalogId=@id


                LIMIT 1";





            cmd.Parameters.AddWithValue("@id", model.CatalogId);





            var unit = await cmd.ExecuteScalarAsync();





            if (unit != null && unit != DBNull.Value)


            {


                model.HasExistingCard = true;


                model.ExistingCardInfo =


                    "Genel malzeme stok karti mevcut. Birim: "


                    + Convert.ToString(unit);


                return;


            }


        }





        cmd.Parameters.Clear();


        cmd.CommandText = @"


            SELECT COUNT(*)


            FROM MCMaterialStockCardLinks


            WHERE MaterialCatalogId=@id";





        cmd.Parameters.AddWithValue("@id", model.CatalogId);





        if (Convert.ToInt64(


            await cmd.ExecuteScalarAsync() ?? 0L) > 0)


        {


            model.HasExistingCard = true;


            model.ExistingCardInfo =


                "Bu malzeme hammadde stok kartina bagli.";


        }


    }





     
    // MC_FULL_CATALOG_FIELDS_V2
    private static async Task<
        System.Collections.Generic.List<
        System.Collections.Generic.KeyValuePair<string, string>>>
        LoadCatalogFields(SqliteConnection db, int catalogId)
    {
        var result = new System.Collections.Generic.List<
            System.Collections.Generic.KeyValuePair<string, string>>();

        await using var command = db.CreateCommand();

        command.CommandText = @"
            SELECT *
            FROM MCMaterialCatalog
            WHERE Id = @id
            LIMIT 1";

        command.Parameters.AddWithValue("@id", catalogId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return result;

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);

            var value = await reader.IsDBNullAsync(i)
                ? ""
                : Convert.ToString(reader.GetValue(i)) ?? "";

            result.Add(
                new System.Collections.Generic.KeyValuePair<string, string>(
                    name,
                    value
                ));
        }

        return result;
    }

    private SqliteConnection Open()



    {



        return new SqliteConnection(



            _db.Database.GetConnectionString());



    }







    private static async Task<MCInventoryCardInput?> FindMaterial(



        SqliteConnection db,



        int id)



    {



        await using var command = db.CreateCommand();







        command.CommandText = @"



            SELECT



                Id,



                COALESCE(MaterialCode, ''),



                COALESCE(MaterialName, ''),



                COALESCE(MaterialType, '')



            FROM MCMaterialCatalog



            WHERE Id = @id";







        command.Parameters.AddWithValue("@id", id);







        await using var reader = await command.ExecuteReaderAsync();







        if (!await reader.ReadAsync())



            return null;







        return new MCInventoryCardInput



        {



            CatalogId = reader.GetInt32(0),



            Code = reader.GetString(1).Trim(),



            Name = reader.GetString(2).Trim(),



            Type = reader.GetString(3).Trim()



        };



    }



}







public class MCInventoryCardInput



{
    public System.Collections.Generic.List<
        System.Collections.Generic.KeyValuePair<string, string>>
        CatalogFields { get; set; } = new();





    public int CatalogId { get; set; }



    public string Code { get; set; } = "";



    public string Name { get; set; } = "";



    public string Type { get; set; } = "";



    public string Unit { get; set; } = "";



    public bool HasExistingCard { get; set; }


    public string ExistingCardInfo { get; set; } = "";


    public string Error { get; set; } = "";



}




public class MCInventoryListRow
{
    public int CatalogId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Unit { get; set; } = "";
}
