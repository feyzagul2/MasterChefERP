(()=>{
"use strict";
const root=document.getElementById("mc-bankcash-management-summary");if(!root)return;
const s=document.getElementById("mc-bankcash-status"),accounts=document.getElementById("mc-bankcash-accounts"),today=document.getElementById("mc-bankcash-today"),recent=document.getElementById("mc-bankcash-recent"),issues=document.getElementById("mc-bankcash-issues");
const money=(v,c)=>v===null||v===undefined?"—":new Intl.NumberFormat("tr-TR",{style:"currency",currency:c||"TRY",maximumFractionDigits:2}).format(v);
fetch("/api/mc-executive-bankcash-summary",{credentials:"same-origin"}).then(r=>{if(!r.ok)throw new Error("HTTP "+r.status);return r.json();}).then(d=>{
s.textContent=d.hasData?"CANLI VERİ":"HAREKET VERİSİ YOK";
issues.textContent=d.invalidCount>0?("Hatalı hareket: "+d.invalidCount):d.transferIssues>0?("Sorunlu virman: "+d.transferIssues):"Kontrol sorunu yok";
if(!d.hasData){accounts.innerHTML="<div class='mc-finance-empty'>Gerçek banka/kasa hareketi bulunmuyor. Bakiye olarak 0 gösterilmiyor.</div>";today.innerHTML="—";recent.innerHTML="Kayıt yok.";return;}
accounts.innerHTML=d.accounts.map(a=>"<div class='mc-fin-account-row'><div><strong>"+a.name+"</strong><small>"+a.code+" · "+a.accountType+" · "+a.currency+"</small></div><b>"+money(a.balance,a.currency)+"</b></div>").join("");
today.innerHTML="<div>Dış giriş: <b>"+money(d.externalIn,"TRY")+"</b></div><div>Dış çıkış: <b>"+money(d.externalOut,"TRY")+"</b></div><div>Virman giriş: <b>"+money(d.transferIn,"TRY")+"</b></div><div>Virman çıkış: <b>"+money(d.transferOut,"TRY")+"</b></div>";
recent.innerHTML=d.recent.length?d.recent.map(x=>"<div class='mc-fin-recent-row'><div><strong>"+x.account+"</strong><small>"+x.date+" · "+x.description+"</small></div><b>"+(x.direction==="In"?"+ ":"- ")+money(x.amount,x.currency)+"</b></div>").join(""):"Kayıt yok.";
}).catch(()=>{s.textContent="VERİ ALINAMADI";accounts.innerHTML="Finans özeti yüklenemedi.";});
})();
