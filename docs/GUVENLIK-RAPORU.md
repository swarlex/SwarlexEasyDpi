# Güvenlik incelemesi (2026-10-08)

İncelenen sürümler: her reponun 2026-10-07 tarihli ana dalı ve en son resmi sürümleri.

## Özet

Hiçbir repoda kötü amaçlı kod, gizli indirme, telemetri veya arka kapı bulunmadı. Ancak birkaçında gerçek güvenlik zayıflıkları var. SwarlexEasyDpi bunları düzeltiyor.

| # | Repo | Bulgu | Önem | SwarlexEasyDpi'de |
|---|---|---|---|---|
| 1 | romanvht/ByeByeDPI | Android uygulamasında yüksek önemde bir sorun bulundu. Ayrıntılar, geliştiricisine özel olarak bildirilip düzeltildikten sonra eklenecek. | **Yüksek** | SwarlexEasyDpi ByeByeDPI kullanmaz (yalnız Windows) |
| 2 | Pudochu, cagritaskn | Servis exe'si, kullanıcının yazabildiği klasörden (Masaüstü/İndirilenler) **SYSTEM** olarak çalıştırılıyor. O klasöre yazabilen herhangi bir program exe'yi değiştirip bir sonraki açılışta SYSTEM yetkisi alır. | Orta | Motor exe içine gömülüdür, `C:\Program Files\SwarlexEasyDpi\bin` altına çıkarılır (yalnız yönetici yazabilir) ve yazıldıktan sonra gömülü kopyayla karşılaştırılır. Program başka klasörden açılırsa kendini Program Files'a kopyalayıp oradan çalışır (kopyalanan dosyanın çalışan exe ile aynı dosya olduğu doğrulanır). Ayar dosyası yalnız yerleşik listeden bir sıra numarası tutar |
| 3 | hufrea/byedpi (`dist/windows/service_install.bat`) | ByeDPI varsayılan olarak `0.0.0.0:1080`'de dinler; servis betiği adres vermiyor. Güvenlik duvarı izin verirse ağdaki herkes kimlik doğrulamasız proxy olarak kullanabilir. | Orta | Windows programı ByeDPI kullanmıyor (yalnız Android) |
| 4 | cagritaskn, Pudochu, tm4to/easydpi | DNS yönlendirmesi Yandex'e (`77.88.8.8:1253`) **şifresiz** gidiyor; easydpi'nin varsayılanı tüm düz DNS'i Quad9'a yolluyor. İSS sorguları görebilir; bazı ağlarda dışarıya 53/1253. port kapalı olduğu için tüm DNS kırılıyor. | Düşük / gizlilik | Düz DNS yönlendirmesi kaldırıldı. Gerekirse DNS sorguları uygulama içinde **şifreli** (DNS-over-HTTPS, Cloudflare; yedek Google) cevaplanır; sistem DNS ayarı değişmez. Panelde ve ipucunda görünür |
| 5 | Pudochu (`ServisiBASLAT.vbs`) | Başlangıç klasörüne kısayol ekliyor; her açılışta UAC soruyor. Exe'yi göreli yol (`x86_64\goodbyedpi.exe`) ile çağırıyor, başlangıçta çalışma klasörü farklı olduğu için büyük olasılıkla çalışmıyor. README ile dosya adları uyuşmuyor (`Servisi Baslat.cmd` yok). | Düşük | Windows ile başlatma Görev Zamanlayıcı'dan (UAC sormadan, pil ayarları kapalı) Program Files'taki kopyayı çalıştırır |
| 6 | romanvht/ByeByeDPI | `MANAGE_EXTERNAL_STORAGE` ve `QUERY_ALL_PACKAGES` izinleri isteniyor (dosya seçici ve uygulama bazlı tünel için). Geniş izinler ama kodda kötüye kullanım yok. | Bilgi | Değiştirilmedi |
| 8 | tm4to/easydpi | GoodbyeDPI'nin "latest" sürümünü hash kontrolü olmadan indirip çalıştırıyor; `--dnsv6-port 1111` hatalı (Cloudflare o portta DNS sunmuyor); motoru görünür konsol penceresiyle başlatıyor; Discord kurulum dosyasını indirip çalıştırıyor. | Orta | Sabit, kaynaktan derlenmiş GoodbyeDPI; port hatası düzeltildi; pencere açılmaz; Discord indirme alınmadı |

## İkili dosya doğrulaması

Pudochu ve cagritaskn repolarındaki tüm `.exe/.dll/.sys` dosyaları ValdikSS'in resmi `goodbyedpi-0.2.3rc3-2.zip` sürümüyle **birebir aynı** (SHA256):

```
8d412b09…13cc31cd  x86_64/goodbyedpi.exe
6110bfa4…995e6c7e  x86_64/WinDivert.dll
e69b5ba3…c82cf50   x86_64/WinDivert64.sys
66e202c9…c430dd1f  x86/goodbyedpi.exe
625ffdd9…15dd8e5   x86/WinDivert.dll
29ca5ceb…82fcdca   x86/WinDivert32.sys
```

SwarlexEasyDpi GoodbyeDPI'yi C kaynağından kendisi derler. İmzalı olması gereken WinDivert sürücüsü resmi GoodbyeDPI sürümünden alınır; tam hash'ler `build.ps1` içinde sabitlidir ve derlemede kontrol edilir.

## Kaynak kod karşılaştırması

- **cagritaskn/GoodbyeDPI-Turkey `src/`**: ValdikSS ile yorumlar ve boşluklar dışında aynı. Tek kod farkı `service.h`'de bildirim düzeltmesi (`service_register(int argc, char *argv[])`), davranış değişikliği yok. macOS betikleri ayrı bir port, Windows'u etkilemiyor.
- **hufrea/byedpi**: Ağ bağlantısı yalnız proxy işlevi için; kötü amaçlı kod yok.
- **romanvht/ByeByeDPI**: Ağ isteği yalnız kullanıcının başlattığı site testinde (`https://<site>`) ve bağış bağlantılarında. Analitik/izleme kütüphanesi yok (bağımlılıklar: androidx, material, gson).

## Sınırlar

- SwarlexEasyDpi yönetici olarak çalışır. Aynı kullanıcı hesabında çalışan (yönetici olmayan) bir zararlı yazılıma karşı tam koruma sağlanamaz: program ilk kez İndirilenler gibi yazılabilir bir klasörden açılırsa, .NET `Main`'den önce o klasördeki `SwarlexEasyDpi.exe.config` dosyasını okur ve çalışma zamanı bazı DLL'leri yine o klasörden yükleyebilir; kullanıcı ortamındaki `COR_PROFILER` / `COMPlus_*` değişkenleri de etkilidir. Programı ilk kez çalıştırmadan önce bilmediğiniz dosyaların bulunmadığı bir klasöre koymanız önerilir. Tam çözüm imzalı bir kurulum programı ya da arayüzün yönetici olmadan, motorun ayrı bir hizmette çalışmasıdır (gelecek sürüm).
- Aynı kullanıcının yönetici olmayan bir süreci, oturum başına kilit nesnesini önceden oluşturarak programın açılmasını engelleyebilir (yalnız hizmet engelleme, yetki yükseltme değil).
- Önceki sürümde bulunan ve düzeltilen: yönetici olarak çalışan `curl.exe` kullanıcının yazabildiği `%APPDATA%\_curlrc` dosyasını okuyordu (dosya okuma/yazma ile yetki yükseltme). Bağlantı denetimi artık uygulama içinden yapılır; dış program olarak yalnız System32'deki `schtasks.exe` ve `notepad.exe` tam yolla, kabuk kullanılmadan çalıştırılır.
- Bu inceleme statik okumadır. GoodbyeDPI ve WinDivert'in C kodu baştan sona denetlenmedi; yalnız Türkiye çatallarındaki farklar ve resmi binary eşleşmesi kontrol edildi.
