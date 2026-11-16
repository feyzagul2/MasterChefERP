# MASTER CHEF ERP / TAKİP

MASTER CHEF ERP, üretim yapan işletmeler için geliştirilmiş **stok, üretim, depo, palet, sevkiyat, satın alma, cari hesap ve finans yönetim sistemi**dir.

Uygulama ASP.NET Core MVC altyapısı üzerinde geliştirilmiştir ve üretimden sevkiyata kadar operasyonların tek sistem üzerinden takip edilmesini amaçlar.

---

## Özellikler

### Operasyon Merkezi

Sistemdeki temel operasyon modüllerine merkezi erişim sağlar.

- Üretim
- Paketleme
- Stok
- Palet
- Sevkiyat
- Satın alma
- Cari
- Finans
- Raporlama

---

## Hammadde ve Stok Yönetimi

- Hammadde kartları
- Hammadde depoları
- Depo bazlı stok takibi
- Stok sayımı
- Hammadde depo transferleri
- Mal kabul sonrası stok hareketleri
- Stok hareket geçmişi
- Malzeme / stok kartı eşleştirme
- Kritik stok kontrolü

---

## Satın Alma

Satın alma süreci aşağıdaki akış üzerinden yönetilebilir:

1. Satın Alma
2. Alış İrsaliyeleri
3. Mal Kabul
4. Fatura Girişi
5. Fatura Kontrolü
6. Cari Aktarım
7. Cari Kontrol
8. Mutabakat

Ayrıca:

- Tedarikçi cari eşleştirme
- Fatura / cari kontrolü
- Mal kabul ve stok entegrasyonu
- Satın alma denetim raporları

bulunmaktadır.

---

## Üretim Yönetimi

- Üretim reçeteleri
- Üretim emirleri
- Üretim planlama
- Gerçekleşen hammadde tüketimleri
- Üretim tamamlama
- Mamul stok girişleri
- Üretim / paketleme bağlantıları
- Üretim maliyet ve tüketim takibi

Temel üretim akışı:

**Üretim Emri → Üretimi Tamamla → Ambalajlamaya Geç → Palet**

---

## Paketleme

Üretim sonrası mamulün paketlenmesini ve palet stoklarına aktarılmasını sağlar.

- Paketleme kayıtları
- Koli ve adet hesaplamaları
- Ürün / lot bağlantısı
- Palet oluşturma
- Mamul stok hareketleri

---

## Palet Yönetimi

- Palet oluşturma
- Barkod üretimi
- Barkod ile palet bulma
- Kamera ile barkod okutma
- Palet içeriği görüntüleme
- Palet içeriği düzenleme
- Palet etiketi yazdırma
- Palet lokasyon yönetimi
- Palet transferleri
- Palet aktif / pasif yönetimi
- Yetkili kullanıcılar için palet silme

---

## Lot / SKT / Kalite

- Ürün lot takibi
- Son kullanma tarihi takibi
- Kalite kontrol kayıtları
- Sevkiyat öncesi lot kontrolleri
- Ürün izlenebilirliği

---

## Sevkiyat Yönetimi

- Sevkiyat oluşturma
- Palet bazlı sevkiyat
- Sevkiyat paketleri
- Sevkiyat kalemleri
- Sevkiyat onayı
- Stok düşümü
- Sevkiyat iptali ve kontrol mekanizmaları

---

## Cari Hesap Yönetimi

- Müşteri ve tedarikçi cari hesapları
- Borç / alacak hareketleri
- Cari hareket geçmişi
- Adres bilgileri
- İletişim bilgileri
- Banka hesapları
- Fatura kaynaklı cari hareketler
- Cari kontrol ve mutabakat

---

## Banka ve Kasa

- Banka hesapları
- Kasa hesapları
- Hesap hareketleri
- Virman işlemleri
- Banka / kasa raporları
- Finansal kontrol ekranları
- Cari / finans bağlantı kontrolleri

---

## Raporlama

Sistem içerisinde operasyonel ve finansal raporlama ekranları bulunmaktadır.

Örnekler:

- Hammadde stok raporu
- Üretim raporları
- Palet hareketleri
- Sevkiyat raporları
- Cari hesap raporları
- Banka / kasa hareket raporları
- Finans kontrol raporları
- İşlem geçmişi
- Denetim kayıtları

---

## Kullanıcı, Rol ve Yetki Yönetimi

ASP.NET Core Identity altyapısı kullanılmaktadır.

- Kullanıcı yönetimi
- Rol yönetimi
- Modül bazlı yetkilendirme
- Kritik operasyonlarda yetki kontrolü
- Başarısız giriş kilitleme
- Güvenli authentication cookie
- Rol / yetki değişikliklerinin hızlı uygulanması

---

## Güvenlik

Projede çeşitli güvenlik kontrolleri bulunmaktadır:

- ASP.NET Core Identity
- Rol ve izin tabanlı yetkilendirme
- Secure / HttpOnly authentication cookie
- Başarısız giriş kilitleme
- SQLite Foreign Key kontrolü
- Duplicate işlem korumaları
- Kritik stok hareketi kontrolleri
- İşlem geçmişi / audit kayıtları

---

## Teknolojiler

- .NET 10
- ASP.NET Core MVC
- ASP.NET Core Identity
- Entity Framework Core
- SQLite
- Razor Views
- C#
- JavaScript
- CSS
- Bootstrap

---

## Gereksinimler

Projeyi çalıştırmak için:

- .NET 10 SDK
- Git
- Modern bir web tarayıcısı

gereklidir.

---

## Kurulum

Repoyu klonlayın:

    git clone https://github.com/feyzagul2/MasterChefERP.git

Proje klasörüne girin:

    cd MasterChefERP

Bağımlılıkları yükleyin:

    dotnet restore

Projeyi derleyin:

    dotnet build

Uygulamayı çalıştırın:

    dotnet run

---

## Veritabanı

Uygulama SQLite kullanmaktadır.

Gerçek işletme veritabanı GitHub reposuna dahil edilmez.

Aşağıdaki dosyalar public repoda tutulmamalıdır:

- app.db
- *.db-wal
- *.db-shm
- *.sqlite
- *.sqlite3
- Yedek veritabanları
- Gerçek müşteri / tedarikçi verileri
- Finansal kayıtlar

---

## Gizli ve Yerel Dosyalar

Aşağıdaki içerikler `.gitignore` aracılığıyla repository dışında tutulmalıdır:

- bin/
- obj/
- Backups/
- Logs/
- publish-check/
- *.db
- *.log
- .env
- secrets.json
- Sertifika ve private key dosyaları

---

## Veri Gizliliği

Bu repository kaynak kod paylaşımı amacıyla hazırlanmıştır.

Repository içerisinde gerçek:

- müşteri kayıtları
- tedarikçi kayıtları
- cari bakiyeler
- banka hareketleri
- üretim kayıtları
- stok kayıtları
- sevkiyat kayıtları

bulundurulmamalıdır.

---

## Proje Durumu

Aşağıdaki teknik kontroller tamamlanmıştır:

- Uygulama build kontrolü
- Authentication
- Authorization
- Kullanıcı / rol kontrolleri
- Veritabanı integrity kontrolü
- Foreign Key koruması
- Kritik route kontrolleri
- Runtime ve log kontrolleri
- Performans kontrolleri
- Release publish kontrolü
- Temel UI ve kullanım akışları

Gerçek işletme işlemleri kullanıldıkça operasyonel doğrulamalar ayrıca yapılabilir.

---

## Önemli Not

Bu proje gerçek işletme süreçlerini yönetebilecek modüller içerir.

Production ortamına alınmadan önce:

- kendi veritabanınızı oluşturun,
- kullanıcı ve yetkileri yapılandırın,
- production ayarlarını ayrı tutun,
- düzenli yedekleme politikası uygulayın,
- gerçek şifre veya secret bilgilerini repository içerisine eklemeyin.

---

## Repository

**MasterChefERP**

GitHub:

https://github.com/feyzagul2/MasterChefERP
