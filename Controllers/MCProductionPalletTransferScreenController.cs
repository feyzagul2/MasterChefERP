using MasterChefTakip.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MasterChefTakip.Controllers;

[Authorize(Policy = AppPermissions.ProductionManage)]
[ApiController]
[Route("mc-production-pallet-transfer-screen")]
public sealed class MCProductionPalletTransferScreenController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public MCProductionPalletTransferScreenController(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private SqliteConnection OpenReadOnly()
    {
        var value = _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Veritabani baglantisi yok.");

        var builder = new SqliteConnectionStringBuilder(value);

        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource == ":memory:")
            throw new InvalidOperationException("Gecersiz veritabani yolu.");

        var actual = Path.GetFullPath(
            Path.IsPathRooted(builder.DataSource)
                ? builder.DataSource
                : Path.Combine(
                    _environment.ContentRootPath,
                    builder.DataSource));

        var expected = Path.GetFullPath(
            Path.Combine(_environment.ContentRootPath, "app.db"));

        if (!string.Equals(
            actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Beklenmeyen veritabani baglantisi.");

        return new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = expected,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
    }

    // MC_PRODUCTION_CSRF_ENDPOINT_V2
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery()
    {
        var service =
            Microsoft.Extensions.DependencyInjection
                .ServiceProviderServiceExtensions
                .GetRequiredService<
                    Microsoft.AspNetCore.Antiforgery.IAntiforgery
                >(HttpContext.RequestServices);

        var tokens = service.GetAndStoreTokens(HttpContext);

        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            return StatusCode(500, new
            {
                error = "Guvenlik anahtari olusturulamadi."
            });
        }

        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            requestToken = tokens.RequestToken
        });
    }

    [HttpGet("data")]
    public IActionResult Data()
    {
        using var db = OpenReadOnly();
        db.Open();

        var receipts = new List<object>();
        var pallets = new List<object>();

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    r.Id,
                    COALESCE(r.ProductName,''),
                    r.ActualKg,
                    r.CardId,
                    COALESCE(p.Name,''),
                    p.DefaultUnitNetWeightGrams,
                    p.DefaultUnitsPerBox,
                    COALESCE((
                        SELECT SUM(l.AllocatedUnits)
                        FROM MCProductionPalletLinks l
                        WHERE l.ReceiptId = r.Id
                    ),0)
                FROM MCProductionFinishedGoodsReceipts r
                LEFT JOIN Products p
                  ON r.CardTable = 'Products'
                 AND p.Id = r.CardId
                ORDER BY r.Id DESC
                LIMIT 300
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var id = Convert.ToInt64(reader.GetValue(0));
                var productName = reader.IsDBNull(1)
                    ? "" : reader.GetString(1);

                var actualKg = reader.IsDBNull(2)
                    ? 0m
                    : Convert.ToDecimal(
                        reader.GetValue(2),
                        CultureInfo.InvariantCulture);

                var productId = reader.IsDBNull(3)
                    ? 0L : Convert.ToInt64(reader.GetValue(3));

                var cardName = reader.IsDBNull(4)
                    ? "" : reader.GetString(4);

                var unitGrams = reader.IsDBNull(5)
                    ? 0m
                    : Convert.ToDecimal(
                        reader.GetValue(5),
                        CultureInfo.InvariantCulture);

                var unitsPerBox = reader.IsDBNull(6)
                    ? 0 : Convert.ToInt32(reader.GetValue(6));

                var allocated = reader.IsDBNull(7)
                    ? 0L : Convert.ToInt64(reader.GetValue(7));

                long totalUnits = 0;

                if (actualKg > 0 && unitGrams > 0)
                {
                    var calculated = decimal.Floor(
                        actualKg * 1000m / unitGrams);

                    if (calculated <= long.MaxValue)
                        totalUnits = (long)calculated;
                }

                var ready =
                    productId > 0 &&
                    unitGrams > 0 &&
                    unitsPerBox > 0 &&
                    actualKg > 0 &&
                    allocated >= 0 &&
                    allocated <= totalUnits;

                receipts.Add(new
                {
                    id,
                    productName = string.IsNullOrWhiteSpace(cardName)
                        ? productName : cardName,
                    actualKg,
                    productId,
                    unitGrams,
                    unitsPerBox,
                    totalUnits,
                    allocatedUnits = allocated,
                    remainingUnits = ready
                        ? totalUnits - allocated : 0,
                    ready
                });
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id
                FROM Pallets
                ORDER BY Id DESC
                LIMIT 500
                """;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var id = Convert.ToInt64(reader.GetValue(0));

                pallets.Add(new
                {
                    id,
                    label = "Palet #" + id
                });
            }
        }

        return Ok(new
        {
            success = true,
            receipts,
            pallets
        });
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return Content("""
<!doctype html>
<html lang="tr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>MASTER CHEF | Mamul Palet Aktarımı</title>
<style>
:root{font-family:Arial,Helvetica,sans-serif;color:#172033;background:#f4f6fa}
*{box-sizing:border-box}
body{margin:0}
header{background:#17243d;color:white;padding:22px 28px}
header h1{font-size:23px;margin:0 0 7px}
header p{margin:0;color:#dce5f2;font-size:14px}
main{max-width:1120px;margin:26px auto;padding:0 18px}
.top{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap}
.card{background:white;border:1px solid #e1e6ef;border-radius:13px;padding:22px;margin-top:18px;box-shadow:0 3px 12px #15203b0a}
h2{font-size:18px;margin:0 0 18px}
.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}
label{display:block;font-weight:600;font-size:14px;margin-bottom:8px}
input,select{width:100%;border:1px solid #ccd4e0;border-radius:8px;padding:12px;font-size:15px;background:white}
input:focus,select:focus{outline:2px solid #87a7e8}
.stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin-top:20px}
.stat{background:#f5f7fc;padding:16px;border-radius:10px}
.stat small{display:block;color:#64748b;margin-bottom:10px}
.stat strong{font-size:23px}
.actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:18px}
button,.link{border:0;border-radius:9px;padding:12px 18px;font-weight:600;cursor:pointer;font-size:14px}
.primary{background:#1c5cba;color:white}
.secondary{background:#e8eef8;color:#183c77}
button:disabled{opacity:.55;cursor:not-allowed}
.link{text-decoration:none;background:#e8eef8;color:#183c77}
.message{margin-top:15px;padding:14px;border-radius:9px;display:none;white-space:pre-wrap}
.success{background:#e8f8ed;color:#13652f}
.error{background:#ffedef;color:#9d1828}
.warning{background:#fff7e4;color:#7b5709}
.note{font-size:13px;color:#627083;line-height:1.6;margin-top:16px}
@media(max-width:740px){.grid{grid-template-columns:1fr}.stats{grid-template-columns:repeat(2,1fr)}}
</style>
</head>
<body>
<header>
  <h1>MASTER CHEF | Mamul → Palet Aktarımı</h1>
  <p>Üretimden mamul stokuna güvenli koli ve adet aktarımı</p>
</header>
<main>
  <div class="top">
    <a class="link" href="/ProductionPackaging">← Üretim ambalajlama</a>
    <button type="button" class="secondary" id="reload">Verileri yenile</button>
  </div>

  <section class="card">
    <h2>1. Mamul girişi ve palet seçimi</h2>
    <div class="grid">
      <div>
        <label for="receipt">Üretimden gelen mamul</label>
        <select id="receipt"><option value="">Yükleniyor...</option></select>
      </div>
      <div>
        <label for="pallet">Aktarılacak palet</label>
        <select id="pallet"><option value="">Yükleniyor...</option></select>
      </div>
    </div>
    <div class="stats">
      <div class="stat"><small>Üretilen adet</small><strong id="total">—</strong></div>
      <div class="stat"><small>Aktarılan adet</small><strong id="allocated">—</strong></div>
      <div class="stat"><small>Kalan adet</small><strong id="remaining">—</strong></div>
      <div class="stat"><small>Koli içi adet</small><strong id="perBox">—</strong></div>
    </div>
    <p class="note" id="detail">Mamul seçildiğinde gramaj ve üretim bilgileri görüntülenir.</p>
  </section>

  <section class="card">
    <h2>2. Aktarım miktarı</h2>
    <div class="grid">
      <div>
        <label for="boxes">Tam koli</label>
        <input id="boxes" type="number" min="0" step="1" value="0">
      </div>
      <div>
        <label for="loose">Açık adet</label>
        <input id="loose" type="number" min="0" step="1" value="0">
      </div>
    </div>
    <div class="stats">
      <div class="stat"><small>İstenen aktarım</small><strong id="requested">0</strong></div>
      <div class="stat"><small>Aktarım sonrası kalan</small><strong id="after">—</strong></div>
    </div>
    <div class="actions">
      <button type="button" class="primary" id="submit" disabled>Aktarımı Onayla</button>
      <span id="busy"></span>
    </div>
    <div id="message" class="message" role="status"></div>
    <p class="note">Onaylanan aktarım mevcut palete yeni bir palet kalemi, stok giriş hareketi ve mamul–palet bağlantısı oluşturur. Aynı işlem kimliği ikinci kez işlenmez.</p>
  </section>
</main>
<script>
(function(){
  "use strict";
  const $ = id => document.getElementById(id);
  const state = {receipts:[],pallets:[],sending:false};
  const format = n => Number(n || 0).toLocaleString("tr-TR");
  let pendingKey = null;
  let pendingPayload = null;

  function selected(){
    return state.receipts.find(x => String(x.id) === $("receipt").value);
  }

  function numberValue(id){
    const input = $(id);
    if (!input.value.trim()) return NaN;
    const n = Number(input.value);
    return Number.isSafeInteger(n) && n >= 0 ? n : NaN;
  }

  function calculate(){
    const r = selected();
    const box = numberValue("boxes");
    const loose = numberValue("loose");
    const qty = r && Number.isFinite(box) && Number.isFinite(loose)
      ? box * r.unitsPerBox + loose : NaN;

    const valid = !!r && r.ready &&
      $("pallet").value !== "" &&
      Number.isSafeInteger(qty) && qty > 0 &&
      loose < r.unitsPerBox &&
      qty <= r.remainingUnits;

    $("total").textContent = r ? format(r.totalUnits) : "—";
    $("allocated").textContent = r ? format(r.allocatedUnits) : "—";
    $("remaining").textContent = r ? format(r.remainingUnits) : "—";
    $("perBox").textContent = r ? format(r.unitsPerBox) : "—";
    $("requested").textContent = Number.isSafeInteger(qty) ? format(qty) : "—";
    $("after").textContent = r && Number.isSafeInteger(qty)
      ? format(r.remainingUnits - qty) : "—";

    $("detail").textContent = r
      ? "Mamul: " + r.productName + " | Üretim: " + format(r.actualKg) +
        " kg | Birim: " + format(r.unitGrams) + " g" +
        (r.ready ? "" : " | Kart veya miktar bilgisi doğrulanamadı.")
      : "Mamul seçildiğinde üretim bilgileri görüntülenir.";

    $("submit").disabled = !valid || state.sending;
    return {r,box,loose,qty,valid};
  }

  function notice(text,type){
    const box = $("message");
    box.textContent = text;
    box.className = "message " + type;
    box.style.display = "block";
  }

  async function load(){
    $("reload").disabled = true;
    $("submit").disabled = true;
    try{
      const response = await fetch(
        "/mc-production-pallet-transfer-screen/data",
        {credentials:"same-origin",cache:"no-store"}
      );
      if(!response.ok) throw new Error("Veriler alınamadı: HTTP " + response.status);
      const data = await response.json();
      state.receipts = Array.isArray(data.receipts) ? data.receipts : [];
      state.pallets = Array.isArray(data.pallets) ? data.pallets : [];

      const oldReceipt = $("receipt").value;
      const oldPallet = $("pallet").value;

      $("receipt").replaceChildren();
      $("pallet").replaceChildren();

      const add = (select,value,label) => {
        const option = document.createElement("option");
        option.value = String(value);
        option.textContent = label;
        select.appendChild(option);
      };

      add($("receipt"),"","Mamul seçiniz");
      for(const r of state.receipts){
        add($("receipt"),r.id,"#" + r.id + " · " + r.productName +
          " · Kalan: " + format(r.remainingUnits) + " adet");
      }

      add($("pallet"),"","Palet seçiniz");
      for(const p of state.pallets){
        add($("pallet"),p.id,p.label);
      }

      $("receipt").value = state.receipts.some(x=>String(x.id)===oldReceipt)
        ? oldReceipt : "";
      $("pallet").value = state.pallets.some(x=>String(x.id)===oldPallet)
        ? oldPallet : "";

      if(state.receipts.length===0)
        notice("Henüz mamul giriş kaydı bulunmuyor. Önce üretimden mamul girişi oluşturulmalıdır.","warning");
      else if(state.pallets.length===0)
        notice("Henüz palet yok. Önce mevcut palet yönetimi ekranından palet oluşturulmalıdır.","warning");

      calculate();
    } catch(e){
      notice(e.message || "Veriler yüklenemedi.","error");
    } finally {
      $("reload").disabled = false;
    }
  }

  async function submit(){
    const v = calculate();
    if(!v.valid || state.sending) return;

    const payload = {
      receiptId:Number($("receipt").value),
      palletId:Number($("pallet").value),
      boxCount:v.box,
      looseUnitCount:v.loose,
      requestId:crypto.randomUUID()
    };

    if(!confirm(
      format(v.qty) + " adet mamulü seçilen palete aktarmak istiyor musunuz?"
    )) return;

    state.sending = true;
    $("busy").textContent = "Aktarım işleniyor...";
    calculate();

    try{
      // Ağ yanıtı kaybolursa aynı isteği yeniden gönderebilmek için
      // istek kimliği sayfa açık kaldığı sürece korunur.
      if(!pendingKey || JSON.stringify({
        receiptId:pendingPayload.receiptId,
        palletId:pendingPayload.palletId,
        boxCount:pendingPayload.boxCount,
        looseUnitCount:pendingPayload.looseUnitCount
      })!==JSON.stringify({
        receiptId:payload.receiptId,
        palletId:payload.palletId,
        boxCount:payload.boxCount,
        looseUnitCount:payload.looseUnitCount
      })){
        pendingKey = payload.requestId;
      }

      payload.requestId = pendingKey;
      pendingPayload = payload;

      // MC_PRODUCTION_CSRF_FETCH_V2
      const csrfResponse = await fetch(
        "/mc-production-pallet-transfer-screen/antiforgery",
        {
          credentials:"same-origin",
          cache:"no-store"
        }
      );

      if(!csrfResponse.ok){
        throw new Error(
          "Guvenlik anahtari alinamadi: HTTP " +
          csrfResponse.status
        );
      }

      const csrfData = await csrfResponse.json();

      if(!csrfData.requestToken){
        throw new Error("Guvenlik anahtari bos.");
      }

      const response = await fetch(
        "/mc-production-pallet-transfer/allocate",
        {
          method:"POST",
          credentials:"same-origin",
          headers:{"Content-Type":"application/json","RequestVerificationToken":csrfData.requestToken},
          body:JSON.stringify(payload)
        }
      );

      const data = await response.json();

      if(!response.ok){
        if(response.status===409 && data.requestId){
          pendingKey = null;
          pendingPayload = null;
        }
        notice(data.error || "Aktarım reddedildi (HTTP " + response.status + ").","error");
      } else {
        pendingKey = null;
        pendingPayload = null;
        notice("Aktarım başarılı. " + format(data.allocatedUnits) +
          " adet stok kaydedildi. Palet kalemi #" + data.palletItemId,"success");
        $("boxes").value="0";
        $("loose").value="0";
        await load();
      }
    } catch(e){
      notice("Sunucu yanıtı alınamadı. İşlem gerçekleşmiş olabilir. Stok kayıtlarını kontrol etmeden yeni istek oluşturmayın.","warning");
    } finally {
      state.sending = false;
      $("busy").textContent = "";
      calculate();
    }
  }

  for(const id of ["receipt","pallet","boxes","loose"])
    $(id).addEventListener("input",calculate);

  $("submit").addEventListener("click",submit);
  $("reload").addEventListener("click",load);
  load();
})();
</script>
</body>
</html>
""", "text/html; charset=utf-8");
    }
}