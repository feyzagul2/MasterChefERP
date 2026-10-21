# MASTER CHEF ERP

### Üretimden sevkiyata, operasyonun tamamı tek merkezde.

**MASTER CHEF'in üretim, depo ve lojistik süreçleri için geliştirilen kurumsal operasyon yönetim platformu.**

![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-MVC-5C2D91?style=for-the-badge)
![Entity Framework Core](https://img.shields.io/badge/Entity_Framework-Core-6C3483?style=for-the-badge)
![SQLite](https://img.shields.io/badge/SQLite-Database-003B57?style=for-the-badge&logo=sqlite&logoColor=white)
![Repository](https://img.shields.io/badge/Repository-Private-1F2937?style=for-the-badge&logo=github&logoColor=white)

**Üretim** · **Paketleme** · **Stok** · **Palet** · **Sevkiyat** · **İade** · **Raporlama**


---

## 🏢 Proje Hakkında

**MasterChefERP**, işletmenin fiziksel ürün hareketlerini takip etmek, depo operasyonlarını düzenlemek ve yönetim süreçlerine görünürlük kazandırmak amacıyla geliştirilmiş bir **ASP.NET Core MVC** uygulamasıdır.

Ürünlerin paketlenmesinden paletlere yerleştirilmesine, depolar arası hareketlerden sevkiyat ve iade işlemlerine kadar operasyonel kayıtların tutarlı bir akışta yönetilmesini hedefler.

> **Odak:** İzlenebilir iş akışları, kontrollü stok hareketleri ve yetki temelli operasyon yönetimi.

---

## 🚀 Operasyonel Yetenekler

| Modül | Kapsam |
|---|---|
| 📦 **Ürün Yönetimi** | Ürün kartları ve ürün bilgilerinin yönetimi |
| 🏭 **Paketleme** | Paketleme kayıtları ve stok giriş süreçleri |
| 🏷️ **Palet ve Barkod** | Palet bazlı ürün takibi, barkod süreçleri ve transferler |
| 📊 **Stok Takibi** | Koli, tekil ürün ve açık koli miktarlarının izlenmesi |
| 🚚 **Sevkiyat** | Sevkiyat hazırlama, koli düzenleme, onay ve durum takibi |
| 🔄 **İade ve Hasar** | İade kabul, kontrol ve hasar hareketlerinin kaydı |
| 📋 **İşlem Geçmişi** | Operasyonel hareketlerin izlenebilirliği |
| 📈 **Raporlama** | Günlük, haftalık ve aylık hareket ve stok raporları |
| 🔐 **Kullanıcı Yönetimi** | Kimlik doğrulama, roller ve işlem bazlı yetkiler |

---

## ⭐ Neden MasterChefERP?

### Uçtan Uca Operasyon Görünürlüğü
Üretim ve depo süreçleri; sevkiyat, iade ve stok hareketleriyle birlikte tek sistem üzerinden yönetilir.

### Detaylı Stok Yönetimi
Tam koli, açık koli ve tekil ürün miktarları ayrı ayrı değerlendirilerek stok hareketlerinin daha hassas takip edilmesi sağlanır.

### İzlenebilir İşlemler
Stok girişleri, çıkışlar, iadeler, hasarlar ve iptaller ayrı işlem türleri olarak kaydedilir.

### Güvenli Yetkilendirme
Kullanıcıların sistemde gerçekleştirebileceği işlemler, rol ve izin politikalarıyla kontrol edilir.

### Yönetim Odaklı Raporlama
Günlük, haftalık ve aylık raporlarla operasyonel süreçlerin değerlendirilmesi kolaylaştırılır.

---

## 🛠️ Teknoloji Altyapısı

MasterChefERP, modern Microsoft teknolojileri kullanılarak geliştirilmiştir.

| Teknoloji | Kullanım Amacı |
|---|---|
| **.NET 10** | Uygulama platformu |
| **ASP.NET Core MVC** | Web uygulaması mimarisi |
| **C#** | Backend geliştirme |
| **Entity Framework Core** | Veritabanı işlemleri |
| **SQLite** | Veritabanı yönetimi |
| **ASP.NET Core Identity** | Kullanıcı kimlik doğrulama |
| **Razor Views** | Dinamik kullanıcı arayüzleri |
| **Bootstrap** | Responsive arayüz |
| **JavaScript** | İnteraktif ekran işlemleri |

---

## 🏗️ Yazılım Mimarisi

```text
                  MASTER CHEF ERP
                         |
              ASP.NET Core MVC
                         |
         +---------------+---------------+
         |               |               |
     Controllers        Views          Services
         |               |               |
     İş Akışları    Kullanıcı       Yetkilendirme
                    Arayüzü         ve Servisler
         |
     ViewModels
         |
       Models
         |
  Entity Framework Core
         |
       SQLite
```

### Temel Bileşenler

- **Controllers:** Uygulama işlemleri ve iş akışları
- **Views:** Razor tabanlı kullanıcı arayüzü
- **ViewModels:** Ekran ve işlem veri modelleri
- **Models:** Uygulama veri modelleri
- **Services:** Uygulama servisleri ve yetkilendirme
- **Data:** Entity Framework Core ve veritabanı altyapısı

---

## 🔐 Güvenlik ve Yetkilendirme

MasterChefERP, kurumsal operasyonların güvenli şekilde yürütülmesi amacıyla kullanıcı kimlik doğrulama ve işlem bazlı yetkilendirme altyapısına sahiptir.

**Güvenlik özellikleri:**

- ASP.NET Core Identity entegrasyonu
- Kullanıcı ve rol yönetimi
- İşlem bazlı izin politikaları
- Yetkisiz erişim kontrolleri
- Veritabanı işlemlerinde eşzamanlılık korumaları
- Kritik operasyonlarda doğrulama mekanizmaları

---

## ✅ Yazılım Kalitesi ve Testler

MasterChefERP'nin geliştirme sürecinde gerçekleştirilen otomatik testler başarıyla tamamlanmıştır.

### Test Sonuçları

| Metrik | Sonuç |
|---|---|
| **Toplam Otomatik Test** | 65 |
| **Başarılı Test** | 65 |
| **Başarısız Test** | 0 |
| **Hedef Platform** | .NET 10 |

### Test Edilen Alanlar

- Stok miktarı hesaplamaları
- Palet transferleri
- Paketleme işlemleri
- Sevkiyat onay süreçleri
- İade ve hasar işlemleri
- Raporlama hesaplamaları
- Veritabanı eşzamanlılık korumaları
- Kullanıcı yetkilendirme politikaları

> Otomatik testler geliştirme ortamında başarıyla tamamlanmıştır. Canlı ortam testleri ve sunucu doğrulamaları ayrıca gerçekleştirilecektir.

**Not:** Test projesi, şirketin tercihine uygun olarak bu GitHub deposuna dahil edilmemektedir.

---

## 💻 Kurulum ve Çalıştırma

### Gereksinimler

- .NET 10 SDK
- Uygun geliştirme ortamı
- Yerel veritabanı yapılandırması

### Projeyi Derleme

```powershell
dotnet restore MasterChefTakip.csproj
dotnet build MasterChefTakip.csproj -c Release
```

### Uygulamayı Başlatma

```powershell
dotnet run --project MasterChefTakip.csproj
```

**Önemli:** Veritabanı bağlantıları, ilk kullanıcı hesabı ve canlı ortam yapılandırmaları hedef sisteme uygun şekilde ayrıca hazırlanmalıdır.

---

## 🛡️ Veri Gizliliği

MasterChefERP, MASTER CHEF şirketinin iç operasyonlarında kullanılmak üzere geliştirilmiştir.

Bu GitHub deposu yalnızca uygulamanın kaynak kodlarını barındırmak amacıyla hazırlanmıştır.

**Depoya dahil edilmeyen bilgiler:**

- Şirketin gerçek veritabanları
- Ürün ve fiyat listeleri
- Müşteri ve tedarikçi kayıtları
- Ticari işlem verileri
- Veritabanı yedekleri
- Kullanıcı şifreleri
- API anahtarları ve diğer gizli yapılandırmalar
- Yerel geliştirme dosyaları

> **Gizlilik İlkesi:** Kurumsal veriler ve ticari kayıtlar kaynak kod deposundan ayrı tutulur.

---

## 📌 Proje Durumu

| Aşama | Durum |
|---|---|
| Ana uygulama geliştirme | ✅ Tamamlandı |
| Temel operasyon modülleri | ✅ Tamamlandı |
| Kullanıcı yetkilendirme | ✅ Test edildi |
| Otomatik testler | ✅ 65/65 başarılı |
| Release derlemesi | ✅ Başarılı |
| Canlı sunucu yapılandırması | ⏳ Planlandı |
| Canlı ortam doğrulamaları | ⏳ Bekleniyor |

---

## 🏢 Kurumsal Kullanım

MasterChefERP, MASTER CHEF'in üretim, depo ve sevkiyat operasyonlarını dijital ortamda yönetmek amacıyla geliştirilmiş özel bir yazılım projesidir.

Kaynak kodları ve sistem bileşenleri, şirketin yetkilendirmesi dışında üçüncü kişilerle paylaşılmamalıdır.

---

### MASTER CHEF ERP

**Smart Operations. Better Control.**

*Üretimden sevkiyata, her adım kontrol altında.*

**MASTER CHEF · Kurumsal Operasyon Yönetim Sistemi**

