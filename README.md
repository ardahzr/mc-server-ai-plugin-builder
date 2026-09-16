# MC Panel — Minecraft Sunucu Yönetimi & AI Plugin Builder

Minecraft (Spigot) sunucunu tek bir web panelinden yönet ve yapay zekâ ile
dakikalar içinde Spigot plugin'i üret. Fikrini anlat, AI soru sorsun,
blueprint'i onayla — kod üretilsin, Maven ile derlensin, hata çıkarsa
otomatik düzeltilsin ve sunucuna kurulsun.

## Özellikler

- **Canlı konsol** — SignalR ile anında akan sunucu logları, renklendirilmiş
  seviyeler (INFO/WARN/ERROR), tek satırdan komut gönderme.
- **Tek tıkla kontrol** — Başlat / Durdur / Yeniden Başlat, hızlı `list`,
  `tps`, `plugins` komutları.
- **AI Plugin Builder** — Doğal dilde plugin isteğini yaz; AI netleştirme
  soruları sorar, komutları/izinleri/event listener'ları içeren bir
  blueprint çıkarır.
- **Otomatik derleme ve düzeltme** — Üretilen kod Maven ile derlenir; hata
  alırsa çıktı AI'a geri gönderilir ve otomatik olarak düzeltilir (5 denemeye
  kadar).
- **Mevcut plugin'leri düzenleme** — Var olan bir plugin'in kaynak kodunu
  incele, AI ile sohbet ederek değiştir, yeniden derleyip deploy et.

## Teknoloji

| Katman        | Teknoloji                                   |
|---------------|----------------------------------------------|
| Backend       | ASP.NET Core MVC (.NET 10)                    |
| Gerçek zamanlı | SignalR                                      |
| Plugin platformu | Spigot 1.21.1, Java 21, Maven              |
| Yapay zekâ    | Ollama (yerel model), OpenAI veya Anthropic  |
| Arayüz        | Bootstrap 5, Bootstrap Icons, Plus Jakarta Sans |

## Gereksinimler

- .NET 10 SDK
- Java 21 (Spigot sunucusu ve derleme için)
- Maven (plugin derlemesi için)
- Çalışan bir Spigot/Bukkit sunucusu (jar dosyası)
- AI sağlayıcısı: [Ollama](https://ollama.com) (varsayılan, yerel ve
  ücretsiz) **veya** bir OpenAI/Anthropic API anahtarı

## Kurulum

```bash
git clone https://github.com/ardahzr/mc-server-ai-plugin-builder.git
cd mc-server-ai-plugin-builder
dotnet restore
```

`appsettings.json` içindeki `McServer` bölümünü kendi sunucuna göre
düzenle:

```json
{
  "McServer": {
    "ServerPath": "/yol/senin/minecraft/sunucuna",
    "JavaPath": "/usr/lib/jvm/java-21-openjdk/bin/java",
    "JavaHome": "/usr/lib/jvm/java-21-openjdk",
    "JarFile": "spigot-1.21.1.jar",
    "MinRam": "2G",
    "MaxRam": "4G"
  },
  "AI": {
    "Provider": "ollama",
    "OllamaUrl": "http://localhost:11434",
    "OllamaModel": "spigot-codestral"
  }
}
```

AI sağlayıcısı olarak OpenAI veya Anthropic kullanmak istersen `Provider`
alanını `"openai"` / `"anthropic"` yap ve ilgili `OpenAIKey` /
`AnthropicKey` değerini gir. Anahtarları doğrudan `appsettings.json`'a
yazmak yerine `dotnet user-secrets` veya ortam değişkeni kullanman önerilir.

## Çalıştırma

```bash
dotnet run
```

Panel varsayılan olarak `http://localhost:5121` adresinde açılır.

## Proje Yapısı

```
Controllers/     HTTP endpoint'leri (Sunucu, Plugin Builder, Ana Sayfa)
Hubs/            SignalR konsol hub'ı
Services/        Sunucu süreç yönetimi ve AI/derleme mantığı
Views/           Razor sayfaları (Ana Sayfa, Sunucu, Plugin Builder)
wwwroot/         CSS, JS ve statik varlıklar
```

## Lisans

Bu proje kişisel kullanım için geliştirilmiştir. Minecraft, Mojang AB'nin
ticari markasıdır; bu proje Mojang veya Microsoft ile bağlantılı değildir.
