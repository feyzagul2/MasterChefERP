# MASTER CHEF ERP

MASTER CHEF ERP, üretim yapan işletmeler için geliştirilmiş kapsamlı bir üretim, stok, depo, satın alma, palet, sevkiyat, cari ve finans yönetim sistemidir.

Uygulama ASP.NET Core MVC ve SQLite altyapısı kullanılarak geliştirilmiştir.

## Ana Modüller

- Operasyon Merkezi
- Hammadde ve depo yönetimi
- Hammadde stok sayımı ve transferleri
- Satın alma siparişleri
- Alış irsaliyeleri ve mal kabul
- Satın alma faturaları
- Tedarikçi cari entegrasyonu
- Üretim reçeteleri ve üretim emirleri
- Üretim planlama ve tüketim takibi
- Paketleme ve mamul stok yönetimi
- Palet ve barkod yönetimi
- Palet transferleri
- Lot / SKT / kalite kontrol
- Sevkiyat ve stok düşüm işlemleri
- Müşteri ve tedarikçi cari hesapları
- Cari borç / alacak hareketleri
- Banka ve kasa yönetimi
- Virman işlemleri
- Finans kontrol ve mutabakat raporları
- İşlem geçmişi ve denetim ekranları
- Kullanıcı, rol ve yetki yönetimi

## Teknolojiler

- .NET 10
- ASP.NET Core MVC
- ASP.NET Core Identity
- Entity Framework Core
- SQLite
- Razor Views
- C#
- JavaScript
- CSS / Bootstrap

## Temel İş Akışları

### Satın Alma
Satın Alma -> Alış İrsaliyesi -> Mal Kabul -> Fatura -> Cari Aktarım -> Cari Kontrol -> Mutabakat

### Üretim
Üretim Emri -> Üretimi Tamamla -> Paketleme -> Palet

### Sevkiyat
Palet -> Lot / SKT Kontrolü -> Sevkiyat -> Stok Düşümü

## Güvenlik

- ASP.NET Core Identity
- Rol ve izin tabanlı yetkilendirme
- Secure ve HttpOnly authentication cookie
- Başarısız giriş kilitleme
- SQLite Foreign Key koruması
- Duplicate işlem kontrolleri
- Stok ve finans hareketlerinde doğrulama mekanizmaları
- Audit / işlem geçmişi kayıtları

## Kurulum

    git clone https://github.com/feyzagul2/MasterChefERP.git
    cd MasterChefERP
    dotnet restore
    dotnet build
    dotnet run

## Veritabanı

Uygulama SQLite kullanmaktadır. Gerçek işletme veritabanı repository içerisine dahil edilmez.

Public repository içerisinde gerçek müşteri, tedarikçi, stok, üretim, cari, banka veya finans kayıtları bulunmamalıdır.

## Repository Dışında Tutulan Dosyalar

- SQLite veritabanları
- DB WAL / SHM dosyaları
- Backup klasörleri
- Log dosyaları
- Excel çalışma dosyaları
- Yerel analiz ve test çıktıları
- Sertifikalar ve private key dosyaları
- Environment / secret dosyaları
- Build çıktıları

## Proje Durumu

Aşağıdaki teknik kontroller tamamlanmıştır:

- Build kontrolü
- Authentication ve authorization
- Rol / yetki kontrolleri
- Kritik ekran ve route kontrolleri
- Veritabanı integrity kontrolü
- Foreign Key koruması
- Runtime ve log kontrolleri
- Performans kontrolleri
- Release publish kontrolü
- Temel UI ve operasyon akışı kontrolleri

Gerçek işletme işlemleri kullanıldıkça operasyonel doğrulamalar ayrıca yapılabilir.

## Lisans

Bu repository için ayrı bir lisans belirtilmediği sürece tüm hakları saklıdır.
