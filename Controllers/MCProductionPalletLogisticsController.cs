using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-pallet-logistics")]
public sealed class MCProductionPalletLogisticsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletLogisticsController(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private SqliteConnection OpenReadOnly()
    {
        var configured = _configuration.GetConnectionString(
            "DefaultConnection");

        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Veritabani baglantisi bulunamadi.");

        var builder = new SqliteConnectionStringBuilder(configured);

        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource == ":memory:")
            throw new InvalidOperationException(
                "Fiziksel veritabani yolu bulunamadi.");

        var actual = System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(builder.DataSource)
                ? builder.DataSource
                : System.IO.Path.Combine(
                    _environment.ContentRootPath,
                    builder.DataSource));

        var expected = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                _environment.ContentRootPath,
                "app.db"));

        if (!string.Equals(
            actual,
            expected,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Beklenmeyen veritabani baglantisi.");

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = expected,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

        connection.Open();
        return connection;
    }

    private static long Number(
        SqliteDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? 0
            : Convert.ToInt64(
                reader.GetValue(index),
                CultureInfo.InvariantCulture);
    }

    // MC_LOGISTICS_SHIPMENT_NAVIGATION_V2
    [HttpGet("shipment-navigation")]
    public IActionResult ShipmentNavigation()
    {
        var provider = HttpContext.RequestServices.GetService(
            typeof(Microsoft.AspNetCore.Mvc.Infrastructure
                .IActionDescriptorCollectionProvider)
        ) as Microsoft.AspNetCore.Mvc.Infrastructure
            .IActionDescriptorCollectionProvider;

        if (provider == null)
            return Ok(new { available = false, url = (string?)null });

        string? target = null;

        foreach (var descriptor in provider.ActionDescriptors.Items)
        {
            if (descriptor is not
                Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action)
                continue;

            if (!string.Equals(
                action.ControllerName,
                "Shipments",
                StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                action.ControllerName,
                "Shipment",
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(
                action.ActionName,
                "Index",
                StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                action.ActionName,
                "List",
                StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Url.Action(
                action.ActionName,
                action.ControllerName);

            if (!string.IsNullOrWhiteSpace(candidate) &&
                candidate.StartsWith("/", StringComparison.Ordinal) &&
                !candidate.StartsWith("//", StringComparison.Ordinal))
            {
                target = candidate;
                break;
            }
        }

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            available = target != null,
            url = target
        });
    }

    [HttpGet("data")]
    public IActionResult Data(
        [FromQuery] long? palletId = null)
    {
        if (palletId.HasValue && palletId.Value <= 0)
            return BadRequest(new
            {
                error = "Gecersiz palet numarasi."
            });

        using var db = OpenReadOnly();

        var rows = new List<object>();

        long totalPallets;
        long totalPalletItems;

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Pallets";
            totalPallets = Convert.ToInt64(
                cmd.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            cmd.CommandText = "SELECT COUNT(*) FROM PalletItems";
            totalPalletItems = Convert.ToInt64(
                cmd.ExecuteScalar(),
                CultureInfo.InvariantCulture);
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    pi.Id,
                    pi.PalletId,
                    pi.ProductId,
                    COALESCE(p.Name, ''),
                    pi.BoxCount,
                    pi.LooseUnitCount,
                    pi.UnitsPerBox,

                    (
                        SELECT COUNT(*)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.Type = 1
                    ),

                    (
                        SELECT COUNT(*)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.Type = 2
                    ),

                    (
                        SELECT COUNT(*)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.Type = 3
                    ),

                    (
                        SELECT COUNT(*)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.Type = 4
                    ),

                    (
                        SELECT COUNT(*)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.Type IN (5,6)
                    ),

                    (
                        SELECT COUNT(DISTINCT sm.RelatedShipmentId)
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.RelatedShipmentId IS NOT NULL
                    ),

                    COALESCE((
                        SELECT GROUP_CONCAT(
                            DISTINCT sm.RelatedShipmentId
                        )
                        FROM StockMovements sm
                        WHERE sm.PalletItemId = pi.Id
                          AND sm.RelatedShipmentId IS NOT NULL
                    ), ''),

                    (
                        SELECT COUNT(*)
                        FROM MCProductionPalletLinks l
                        WHERE l.PalletItemId = pi.Id
                    ),

                    COALESCE((
                        SELECT SUM(l.AllocatedUnits)
                        FROM MCProductionPalletLinks l
                        WHERE l.PalletItemId = pi.Id
                    ),0)

                FROM PalletItems pi
                LEFT JOIN Products p
                  ON p.Id = pi.ProductId
                WHERE ($palletId IS NULL OR pi.PalletId = $palletId)
                ORDER BY pi.PalletId DESC, pi.Id DESC
                LIMIT 501
                """;

            cmd.Parameters.AddWithValue(
                "$palletId",
                palletId.HasValue
                    ? (object)palletId.Value
                    : DBNull.Value);

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                long itemId = Number(reader, 0);
                long currentPalletId = Number(reader, 1);
                long productId = Number(reader, 2);

                string productName = reader.IsDBNull(3)
                    ? ""
                    : reader.GetString(3);

                long boxes = Number(reader, 4);
                long loose = Number(reader, 5);
                long unitsPerBox = Number(reader, 6);

                long receiptMovements = Number(reader, 7);
                long shipmentMovements = Number(reader, 8);
                long returnMovements = Number(reader, 9);
                long damageMovements = Number(reader, 10);
                long cancellationMovements = Number(reader, 11);
                long relatedShipmentCount = Number(reader, 12);

                string shipmentIds = reader.IsDBNull(13)
                    ? ""
                    : Convert.ToString(reader.GetValue(13)) ?? "";

                long productionLinks = Number(reader, 14);
                long productionAllocatedUnits = Number(reader, 15);

                long initialUnits = 0;
                bool quantityValid = true;

                try
                {
                    initialUnits = checked(
                        boxes * unitsPerBox + loose);
                }
                catch (OverflowException)
                {
                    quantityValid = false;
                }

                string status =
                    shipmentMovements > 0
                        ? "Sevkiyat hareketi mevcut"
                        : receiptMovements > 0
                            ? "Stok girisi mevcut"
                            : "Stok girisi incelenmeli";

                rows.Add(new
                {
                    palletItemId = itemId,
                    palletId = currentPalletId,
                    productId,
                    productName,
                    boxCount = boxes,
                    looseUnitCount = loose,
                    unitsPerBox,
                    recordedPalletUnits = quantityValid
                        ? (long?)initialUnits : null,
                    quantityValid,
                    receiptMovementCount = receiptMovements,
                    shipmentMovementCount = shipmentMovements,
                    returnMovementCount = returnMovements,
                    damageMovementCount = damageMovements,
                    cancellationMovementCount = cancellationMovements,
                    relatedShipmentCount,
                    relatedShipmentIds = shipmentIds,
                    productionLinkCount = productionLinks,
                    productionAllocatedUnits,
                    status
                });
            }
        }

        bool hasMore = rows.Count > 500;

        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            success = true,
            totalPallets,
            totalPalletItems,
            listedItemCount = rows.Count,
            hasMore,
            note =
                "Bu ekran hareketleri listeler. " +
                "Net sevk edilebilir stok hesaplamasi yapmaz.",
            rows
        });
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        Response.Headers.CacheControl = "no-store";

        return Content("""
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>MASTER CHEF | Palet Lojistik Takip</title>
<style>
*{box-sizing:border-box}
body{margin:0;background:#f4f6fa;color:#1c2940;font-family:Arial,sans-serif}
header{background:#182a46;color:white;padding:24px}
header h1{font-size:24px;margin:0 0 8px}
header p{margin:0;color:#d9e4f6;font-size:14px}
main{max-width:1350px;margin:24px auto;padding:0 18px}
.card{background:white;border:1px solid #e1e6ef;border-radius:12px;padding:20px;margin-bottom:18px}
nav{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:18px}
a{color:#1857ad;text-decoration:none;font-weight:600}
nav a{padding:10px 14px;background:white;border-radius:8px}
button{border:0;background:#1857ad;color:white;padding:11px 17px;border-radius:8px;cursor:pointer}
input{padding:10px;border:1px solid #ccd5e2;border-radius:8px}
.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}
.metric{background:#eef3fa;padding:18px;border-radius:9px}
.metric small{display:block;color:#61728b;margin-bottom:8px}
.metric strong{font-size:25px}
.toolbar{display:flex;align-items:center;gap:12px;flex-wrap:wrap}
.tablewrap{overflow-x:auto}
table{width:100%;border-collapse:collapse;min-width:1050px}
th,td{padding:12px;text-align:left;border-bottom:1px solid #e9edf3;font-size:13px}
th{background:#f1f5fa}
.warning{color:#83570a;background:#fff5dc;padding:13px;border-radius:8px;margin-top:15px}
.error{color:#a21d2e;background:#ffe9ed;padding:13px;border-radius:8px}
.muted{color:#66758b;font-size:13px}
@media(max-width:700px){.grid{grid-template-columns:1fr}}
</style>
</head>
<body>
<header>
<h1>MASTER CHEF | Palet Lojistik Takip</h1>
<p>Palet, stok girişi ve sevkiyat hareketlerinin birlikte izlenmesi</p>
</header>
<main>
<nav>
<a href="/mc-production-pallet-transfer-screen">Mamul → Palet Aktarımı</a>
<a href="/ProductionPackaging">Üretim Ambalajlama</a>
<a href="/mc-production-pallet-audit/data">Palet Stok Denetimi</a>
<a id="shipmentManagementLink"
   href="#"
   hidden>Sevkiyat Yönetimine Git</a>
</nav>
<section class="card">
<div class="grid">
<div class="metric"><small>Toplam palet</small><strong id="pallets">—</strong></div>
<div class="metric"><small>Toplam palet kalemi</small><strong id="items">—</strong></div>
<div class="metric"><small>Listelenen kalem</small><strong id="listed">—</strong></div>
</div>
<div class="warning">
Bu ekran stok ve sevkiyat hareketlerini gösterir.
Sevk edilebilir net stok miktarını henüz hesaplamaz.
</div>
</section>

<!-- MC_LOGISTICS_CURRENT_STOCK_PANEL_V2 -->
<section class="card">
<h2>Güncel Palet Stokları</h2>

<p class="muted">
Mevcut palet kayıtlarındaki koli ve adet miktarları.
Bu değerler henüz sevkiyat uygunluğu onayı değildir.
</p>

<div class="grid">
<div class="metric">
<small>Kontrol Edilen</small>
<strong id="stockChecked">—</strong>
</div>

<div class="metric">
<small>Geçerli Kayıt</small>
<strong id="stockValid">—</strong>
</div>

<div class="metric">
<small>Sorunlu Kayıt</small>
<strong id="stockProblems">—</strong>
</div>
</div>

<p id="stockTotal" class="muted" style="margin-top:16px">
Stok bilgileri yükleniyor...
</p>

<div class="tablewrap">
<table>
<thead>
<tr>
<th>Palet</th>
<th>Ürün</th>
<th>Tam Koli</th>
<th>Koli İçi Adet</th>
<th>Tekil Adet</th>
<th>Açık Koli Adedi</th>
<th>Toplam Adet</th>
<th>Kontrol Sonucu</th>
</tr>
</thead>
<tbody id="stockRows"></tbody>
</table>
</div>

<p class="warning">
Stok hareketleri ikinci kez düşülmez.
Sevkiyat uygunluğu ayrıca doğrulanmalıdır.
</p>
</section>


<!-- MC_LOGISTICS_SHIPMENT_ELIGIBILITY_V1 -->
<section class="card">
<h2>Sevkiyat Ön Kontrolü</h2>

<p class="muted">
Paletlerin kayıtlı stok miktarları, sevkiyat geçmişi
ve kontrol gerektiren durumları.
</p>

<div class="grid">
<div class="metric">
<small>Ön Kontrolden Geçen</small>
<strong id="shipPreliminary">—</strong>
</div>

<div class="metric">
<small>İnceleme Gereken</small>
<strong id="shipReview">—</strong>
</div>

<div class="metric">
<small>Stok Bulunmayan</small>
<strong id="shipEmpty">—</strong>
</div>
</div>

<p id="shipMessage" class="muted" style="margin-top:16px">
Sevkiyat ön kontrol bilgileri yükleniyor...
</p>

<div class="tablewrap">
<table>
<thead>
<tr>
<th>Palet</th>
<th>Ürün</th>
<th>Kayıtlı Adet</th>
<th>Sevkiyat Sayısı</th>
<th>Sevkiyat No</th>
<th>Sevkiyat Durum Kodları</th>
<th>Kontrol Sonucu</th>
<th>Uyarılar</th>
</tr>
</thead>
<tbody id="shipRows"></tbody>
</table>
</div>

<div class="warning">
Bu ekran yalnızca ön kontrol yapar.
Ön kontrolden geçen kayıtlar için bile otomatik sevkiyat izni
verilmez. Nihai sevkiyat işlemleri mevcut sevkiyat
modülünden gerçekleştirilmelidir.
</div>
</section>

<section class="card">
<div class="toolbar">
<label for="palletId">Palet No:</label>
<input id="palletId" type="number" min="1" placeholder="Tüm paletler">
<button type="button" id="refresh">Listeyi Getir</button>
</div>
<p id="message" class="muted"></p>
</section>
<section class="card">
<h2>Palet Hareketleri</h2>
<div class="tablewrap">
<table>
<thead>
<tr>
<th>Palet</th>
<th>Kalem</th>
<th>Ürün</th>
<th>Koli</th>
<th>Açık Adet</th>
<th>Palete Kayıtlı Adet</th>
<th>Stok Girişi</th>
<th>Sevkiyat Hareketi</th>
<th>İade</th>
<th>Hasar</th>
<th>İptal</th>
<th>Sevkiyat ID</th>
<th>Durum</th>
</tr>
</thead>
<tbody id="rows"></tbody>
</table>
</div>
</section>
</main>
<script>
(function(){
"use strict";
const $=id=>document.getElementById(id);
const fmt=n=>Number(n||0).toLocaleString("tr-TR");


async function loadCurrentStock(palletFilter){
 const body=$("stockRows");
 body.replaceChildren();

 $("stockChecked").textContent="—";
 $("stockValid").textContent="—";
 $("stockProblems").textContent="—";
 $("stockTotal").textContent="Stok bilgileri yükleniyor...";

 const params=new URLSearchParams();
 params.set("limit","1000");

 if(palletFilter){
  params.set("palletId",String(palletFilter));
 }

 const response=await fetch(
  "/mc-production-pallet-current-stock/data?"+
  params.toString(),
  {
   credentials:"same-origin",
   cache:"no-store"
  }
 );

 if(!response.ok){
  throw new Error(
   "Stok verileri alınamadı. HTTP "+response.status
  );
 }

 const data=await response.json();

 $("stockChecked").textContent=fmt(data.checkedCount);
 $("stockValid").textContent=fmt(data.validCount);
 $("stockProblems").textContent=fmt(data.problemCount);

 const rows=Array.isArray(data.rows)?data.rows:[];

 for(const item of rows){
  const tr=document.createElement("tr");

  const status=item.valid
   ?"Kayıt geçerli"
   :Array.isArray(item.problems)
      ?item.problems.join("; ")
      :"Kontrol gerekli";

  const values=[
   item.palletId,
   item.productName,
   item.fullBoxes,
   item.unitsPerBox,
   item.looseUnits,
   item.openBoxUnits,
   item.currentRecordedUnits,
   status
  ];

  for(const value of values){
   const td=document.createElement("td");

   td.textContent=
    value===null || value===undefined
     ?"—"
     :String(value);

   tr.appendChild(td);
  }

  body.appendChild(tr);
 }

 const reliable=
  data.totalReliable===true &&
  data.hasMore===false &&
  data.totalRecordedUnits!==null &&
  data.totalRecordedUnits!==undefined;

 if(reliable){
  $("stockTotal").textContent=
   "Kayıtlı toplam: "+
   fmt(data.totalRecordedUnits)+
   " adet (sevkiyat onayı değildir).";
 }
 else{
  $("stockTotal").textContent=
   "Toplam stok doğrulanamadı. "+
   "Eksik sayfa veya sorunlu kayıt bulunabilir.";
 }

 if(data.hasMore){
  $("stockTotal").textContent+=
   " İlk 1000 kayıt gösteriliyor.";
 }
}


async function loadShipmentEligibility(palletFilter){
 const body=$("shipRows");
 body.replaceChildren();

 $("shipPreliminary").textContent="—";
 $("shipReview").textContent="—";
 $("shipEmpty").textContent="—";
 $("shipMessage").textContent="Sevkiyat kontrolleri yükleniyor...";

 const params=new URLSearchParams();
 params.set("limit","1000");

 if(palletFilter){
  params.set("palletId",String(palletFilter));
 }

 const response=await fetch(
  "/mc-production-shipment-eligibility/data?"+params.toString(),
  {
   credentials:"same-origin",
   cache:"no-store"
  }
 );

 if(!response.ok){
  throw new Error(
   "Sevkiyat ön kontrol API hatası: HTTP "+response.status
  );
 }

 const data=await response.json();
 const rows=Array.isArray(data.rows)?data.rows:[];

 let preliminary=0;
 let review=0;
 let empty=0;

 for(const item of rows){

  if(item.status==="ON_KONTROLDEN_GECTI"){
   preliminary++;
  }
  else if(item.status==="STOK_YOK"){
   empty++;
  }
  else{
   review++;
  }

  const warnings=Array.isArray(item.warnings)
   ?item.warnings.join("; ")
   :"";

  const status=
   item.status==="ON_KONTROLDEN_GECTI"
    ?"Ön Kontrolden Geçti"
    :item.status==="STOK_YOK"
     ?"Stok Yok"
     :"İnceleme Gerekli";

  const tr=document.createElement("tr");

  const values=[
   item.palletId,
   item.productName,
   item.currentRecordedUnits,
   item.shipmentCount,
   item.shipmentIds,
   item.shipmentStatusCodes,
   status,
   warnings||"—"
  ];

  for(const value of values){
   const td=document.createElement("td");

   td.textContent=value===null || value===undefined
    ?"—"
    :String(value);

   tr.appendChild(td);
  }

  if(item.status==="INCELEME_GEREKLI"){
   tr.style.backgroundColor="#fff5dc";
  }

  body.appendChild(tr);
 }

 $("shipPreliminary").textContent=fmt(preliminary);
 $("shipReview").textContent=fmt(review);
 $("shipEmpty").textContent=fmt(empty);

 $("shipMessage").textContent=
  rows.length===0
   ?"Kontrol edilecek palet kaydı bulunamadı."
   :data.hasMore
    ?"İlk 1000 kayıt kontrol edildi. Palet filtresi kullanabilirsiniz."
    :fmt(rows.length)+" palet kalemi kontrol edildi.";

 if(data.automaticShipmentAllowed!==false){
  $("shipMessage").textContent+=
   " Uyarı: otomatik sevkiyat engeli doğrulanamadı.";
 }
}

async function loadShipmentNavigation(){
 const link=$("shipmentManagementLink");

 try{
  const response=await fetch(
   "/mc-production-pallet-logistics/shipment-navigation",
   {
    credentials:"same-origin",
    cache:"no-store"
   }
  );

  if(!response.ok){
   return;
  }

  const data=await response.json();

  if(
   data.available===true &&
   typeof data.url==="string" &&
   data.url.startsWith("/") &&
   !data.url.startsWith("//")
  ){
   link.href=data.url;
   link.hidden=false;
  }
 }catch(error){
  // Sevkiyat adresi bulunamazsa stok ekrani calismaya devam eder.
 }
}

async function load(){
 $("refresh").disabled=true;
 $("message").textContent="Veriler yükleniyor...";
 $("rows").replaceChildren();

 try{
  const raw=$("palletId").value.trim();
  const params=new URLSearchParams();

  if(raw){
   const id=Number(raw);
   if(!Number.isSafeInteger(id)||id<=0){
    throw new Error("Geçerli bir palet numarası giriniz.");
   }
   params.set("palletId",String(id));
  }

  const url="/mc-production-pallet-logistics/data"+
   (params.toString()?"?"+params.toString():"");

  const response=await fetch(url,{
   credentials:"same-origin",
   cache:"no-store"
  });

  if(!response.ok){
   throw new Error("Veriler alınamadı. HTTP "+response.status);
  }

  const data=await response.json();

  $("pallets").textContent=fmt(data.totalPallets);
  $("items").textContent=fmt(data.totalPalletItems);
  $("listed").textContent=fmt(data.listedItemCount);
  try{
   await loadShipmentEligibility(raw);
  }catch(shipmentError){
   $("shipMessage").textContent=
    "Sevkiyat ön kontrolü yüklenemedi: "+
    (shipmentError.message||"Bilinmeyen hata");
  }


  try{
   await loadCurrentStock(raw);
  }catch(stockError){
   $("stockTotal").textContent=
    "Stok paneli yüklenemedi: "+
    (stockError.message||"Bilinmeyen hata");
  }


  const columns=[
   "palletId","palletItemId","productName",
   "boxCount","looseUnitCount","recordedPalletUnits",
   "receiptMovementCount","shipmentMovementCount",
   "returnMovementCount","damageMovementCount",
   "cancellationMovementCount","relatedShipmentIds","status"
  ];

  for(const row of data.rows||[]){
   const tr=document.createElement("tr");

   for(const key of columns){
    const td=document.createElement("td");
    const value=row[key];
    td.textContent=value==null?"—":String(value);
    tr.appendChild(td);
   }

   $("rows").appendChild(tr);
  }

  $("message").textContent=
   (data.rows||[]).length===0
    ?"Gösterilecek palet kalemi bulunamadı."
    :data.hasMore
      ?"İlk 500 kayıt gösteriliyor. Palet numarasıyla filtreleyebilirsiniz."
      :"Liste güncellendi.";

 }catch(error){
  $("message").textContent=error.message||"Veriler okunamadı.";
 }finally{
  $("refresh").disabled=false;
 }
}
$("refresh").addEventListener("click",load);
loadShipmentNavigation();
load();
})();
</script>
</body>
</html>
""", "text/html; charset=utf-8");
    }
}