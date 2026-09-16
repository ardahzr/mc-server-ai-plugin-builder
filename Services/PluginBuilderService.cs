using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using McPanel.Models;

namespace McPanel.Services;

public class PluginBuilderService
{
    private readonly ILogger<PluginBuilderService> _logger;
    private readonly IConfiguration _config;
    private readonly string _serverPath;
    private readonly string _javaHome;

    public string ServerPath => _serverPath;

    public PluginBuilderService(IConfiguration config, ILogger<PluginBuilderService> logger)
    {
        _config = config;
        _logger = logger;
        _serverPath = config["McServer:ServerPath"] ?? "/home/libuntu/Desktop/mc-server";
        _javaHome = config["McServer:JavaHome"] ?? "/usr/lib/jvm/java-21-openjdk";
    }

    /// <summary>
    /// AI ile plugin kodu üretir
    /// </summary>
    public async Task<AiResponse> GeneratePluginCode(string userPrompt, string? conversationHistory = null)
    {
        var provider = _config["AI:Provider"] ?? "ollama";
        
        return provider.ToLower() switch
        {
            "ollama" => await CallOllama(userPrompt, conversationHistory),
            "openai" => await CallOpenAI(userPrompt, conversationHistory),
            "anthropic" => await CallAnthropic(userPrompt, conversationHistory),
            _ => await CallOllama(userPrompt, conversationHistory)
        };
    }

    // ========== WIZARD METHODS ==========

    public async Task<List<ClarificationQuestion>> GenerateClarificationQuestions(string description)
    {
        var prompt = "Sen bir Minecraft Spigot plugin gelistiricisin. Kullanici senden plugin istiyor.\n" +
            "Aciklama: \"" + description + "\"\n\n" +
            "AMAC: Plugin'in OYNANIS ve TASARIM detaylarini netlestirecek 5 soru sor.\n\n" +
            "KURALLAR:\n" +
            "- ASLA teknik implementasyon sorma (hangi event, hangi sinif, hangi API, nasil kodlanacak gibi). Bunlari SEN biliyorsun.\n" +
            "- ASLA 'nasil saklayacaksiniz', 'hangi metodu kullanacaksiniz', 'nasil kuracaksiniz' gibi gelistirici sorulari sorma.\n" +
            "- SADECE oyuncu deneyimi, ozellik tercihleri, oyun mekanigi ve icerik sorulari sor.\n" +
            "- Sorular spesifik ve plugin'e ozel olmali, genel olmamali.\n\n" +
            "ORNEK IYI SORULAR (MobKill plugin icin):\n" +
            "- Farkli mob turleri icin farkli odul miktarlari olsun mu? (Orn: Zombie=5, Ender Dragon=500)\n" +
            "- Oldurulen mob sayisini gosteren bir skor tablosu (leaderboard) olsun mu?\n" +
            "- Oduller altin mi, deneyim puani mi, yoksa ikisi birden mi olsun?\n" +
            "- Belirli biyomlarda veya dunya turlerinde farkli carpan olsun mu?\n" +
            "- Gunluk/haftalik odul limiti olsun mu?\n\n" +
            "ORNEK KOTU SORULAR (BUNLARI SORMA):\n" +
            "- Hangi event'i kullanacaksiniz? (HAYIR - sen biliyorsun)\n" +
            "- Verileri nasil saklayacaksiniz? (HAYIR - sen karar ver)\n" +
            "- Oyuncuyu nasil kontrol edeceksiniz? (HAYIR - teknik detay)\n" +
            "- Envanteri nasil elde edeceksiniz? (HAYIR - API bilgisi)\n\n" +
            "SADECE asagidaki JSON formatinda yanit ver, baska hicbir sey yazma:\n" +
            "[\n" +
            "  {\"id\": 1, \"question\": \"Soru metni\", \"placeholder\": \"Ornek cevap\"},\n" +
            "  {\"id\": 2, \"question\": \"Soru metni\", \"placeholder\": \"Ornek cevap\"},\n" +
            "  {\"id\": 3, \"question\": \"Soru metni\", \"placeholder\": \"Ornek cevap\"},\n" +
            "  {\"id\": 4, \"question\": \"Soru metni\", \"placeholder\": \"Ornek cevap\"},\n" +
            "  {\"id\": 5, \"question\": \"Soru metni\", \"placeholder\": \"Ornek cevap\"}\n" +
            "]";

        var response = await CallOllamaRaw(prompt, 0.6, 2048);
        if (!response.Success)
            throw new Exception(response.Error ?? "AI baglanti hatasi");

        var content = response.Content.Trim();
        var jsonMatch = Regex.Match(content, @"\[[\s\S]*\]");
        if (!jsonMatch.Success)
            throw new Exception("AI gecerli JSON uretemedi");

        try
        {
            var questions = JsonSerializer.Deserialize<List<ClarificationQuestion>>(jsonMatch.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            return questions;
        }
        catch
        {
            return new List<ClarificationQuestion>
            {
                new() { Id = 1, Question = "Plugin'in ana komutlari ne olmali ve ne yapsinlar?", Placeholder = "/istatistik, /odul, /ayar..." },
                new() { Id = 2, Question = "Oyuncular icin ozel ozellikler veya bonus mekanikler olsun mu?", Placeholder = "Seri kill bonusu, gunluk gorevler..." },
                new() { Id = 3, Question = "Admin'ler icin ozel yetkiler veya kontrol mekanikleri isteniyor mu?", Placeholder = "Odul miktarini ayarlama, oyuncu sifirlama..." },
                new() { Id = 4, Question = "Mesajlar, odullar gibi degerler config dosyasindan ayarlanabilsin mi?", Placeholder = "Evet, tum degerler config'den ayarlansin" },
                new() { Id = 5, Question = "Skor tablosu, istatistik veya bildirim gibi ekstra ozellikler olsun mu?", Placeholder = "Leaderboard ve kill sayaci" }
            };
        }
    }

    public async Task<PluginBlueprint> GenerateBlueprint(string name, string description, List<ClarificationQuestion> answers)
    {
        var answersText = string.Join("\n", answers.Select(a => $"S: {a.Question}\nC: {a.Answer}"));

        var prompt = $@"Minecraft Spigot 1.21.1 plugin blueprint'i olustur.
Plugin adi: {name}
Aciklama: {description}
Platform: Spigot 1.21.1, Java 21

Kullanici cevaplari:
{answersText}

ONEMLI: EntityDeathEvent.getEntity() LivingEntity doner. BankManager.deposit(UUID, double).

SADECE asagidaki JSON formatinda yanit ver:
{{\n  ""pluginName"": ""{name}"",\n  ""description"": ""Kisa aciklama"",\n  ""general"": {{\n    ""summary"": ""Ozet"",\n    ""detailedDescription"": ""Detayli aciklama"",\n    ""features"": [""Ozellik 1"", ""Ozellik 2""]\n  }},\n  ""commands"": [\n    {{""name"": ""/komut"", ""description"": ""Ne yapar"", ""usage"": ""/komut <arg>"", ""permission"": ""plugin.komut""}}\n  ],\n  ""permissions"": [\n    {{""node"": ""plugin.komut"", ""description"": ""Izin aciklamasi"", ""default"": ""true""}}\n  ],\n  ""userStories"": [\n    {{""role"": ""PLAYER"", ""story"": ""Oyuncu olarak ... yapabilmeliyim""}},\n    {{""role"": ""ADMIN"", ""story"": ""Admin olarak ... yapabilmeliyim""}}\n  ],\n  ""technical"": {{\n    ""eventListeners"": [\n      {{""eventName"": ""EventAdi"", ""description"": ""Ne zaman tetiklenir"", ""verified"": true}}\n    ],\n    ""classes"": [\n      {{""name"": ""MainPlugin"", ""type"": ""MAIN CLASS"", ""description"": ""Ana sinif""}},\n      {{""name"": ""SomeListener"", ""type"": ""EVENT LISTENER CLASS"", ""description"": ""Listener""}}\n    ],\n    ""apiMethods"": [\n      {{""signature"": ""Player.sendMessage(String)"", ""source"": ""Bukkit API"", ""description"": ""Mesaj gonder"", ""verified"": true}}\n    ],\n    ""dependencies"": [""spigot-api 1.21.1""]\n  }}\n}}";

        var response = await CallOllamaRaw(prompt, 0.4, 4096);
        if (!response.Success)
            throw new Exception(response.Error ?? "AI baglanti hatasi");

        var content = response.Content.Trim();
        var jsonMatch = Regex.Match(content, @"\{[\s\S]*\}");
        if (!jsonMatch.Success)
            throw new Exception("AI gecerli blueprint JSON uretemedi");

        try
        {
            var blueprint = JsonSerializer.Deserialize<PluginBlueprint>(jsonMatch.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            blueprint.RawBlueprintJson = jsonMatch.Value;
            if (string.IsNullOrEmpty(blueprint.PluginName)) blueprint.PluginName = name;
            return blueprint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blueprint JSON parse hatasi");
            return new PluginBlueprint
            {
                PluginName = name,
                Description = description,
                RawBlueprintJson = jsonMatch.Value,
                General = new GeneralInfo { Summary = description, Features = new() { description } },
                Technical = new TechnicalDetails
                {
                    Classes = new() { new ClassInfo { Name = name, Type = "MAIN CLASS", Description = "Ana sinif" } }
                }
            };
        }
    }

    public async Task<AiResponse> GenerateCodeFromBlueprint(string pluginName, string blueprintJson)
    {
        var prompt = "Asagidaki blueprint'e gore TAM, DERLENEBILIR Minecraft Spigot 1.21.1 plugin kodu uret.\n\n" +
            "BLUEPRINT:\n" + blueprintJson + "\n\n" +
            "KRITIK KURALLAR:\n" +
            "1. Her dosya icin // FILE: yol/Dosya.java basligi, sonra ```java ``` bloku\n" +
            "2. pom.xml MUTLAKA yaz (groupId: com.example, spigot-api 1.21.1-R0.1-SNAPSHOT provided)\n" +
            "3. plugin.yml MUTLAKA yaz (api-version: '1.21')\n" +
            "4. Her Java dosyasinda package + TUM import satirlari OLMALI\n" +
            "5. EntityDeathEvent.getEntity() LivingEntity doner. getKiller() LivingEntity uzerinde.\n" +
            "6. BankManager.deposit(UUID, double) — Player DEGIL UUID!\n" +
            "7. Kisaltma yapma, TAM dosya icerigini yaz.\n" +
            "8. Spigot repo: https://hub.spigotmc.org/nexus/content/repositories/snapshots/\n" +
            "Ilk dosya pom.xml, sonra plugin.yml, sonra Java siniflari.";

        return await CallOllamaRaw(prompt, 0.3, 16384);
    }

    public async Task<AiResponse> FixCompilationErrors(string code, string errors, string pluginName)
    {
        var prompt = "DERLEME HATASI! Asagidaki hatalari duzelt.\n\n" +
            "HATALAR:\n" + errors + "\n\nMEVCUT KOD:\n" + code + "\n\n" +
            "KRITIK:\n" +
            "1. Her Java dosyasinda package OLMALI\n" +
            "2. Her kullanilan sinif icin import OLMALI\n" +
            "3. LivingEntity kullan, Entity degil (getKiller icin)\n" +
            "4. BankManager.deposit(UUID, double)\n" +
            "5. pom.xml + plugin.yml dahil TUM dosyalari yaz\n" +
            "6. // FILE: basliklarini koru";

        return await CallOllamaRaw(prompt, 0.2, 16384);
    }

    public string GenerateDefaultPom(string pluginName)
    {
        // artifactId olarak plugin adını olduğu gibi kullan (JAR adı bu olur)
        var artifactId = pluginName.Replace(" ", "");
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<project xmlns=""http://maven.apache.org/POM/4.0.0""
         xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
         xsi:schemaLocation=""http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd"">
    <modelVersion>4.0.0</modelVersion>
    <groupId>com.example</groupId>
    <artifactId>{artifactId}</artifactId>
    <version>1.0-SNAPSHOT</version>
    <packaging>jar</packaging>
    <name>{pluginName}</name>
    <properties>
        <maven.compiler.source>21</maven.compiler.source>
        <maven.compiler.target>21</maven.compiler.target>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
    </properties>
    <repositories>
        <repository>
            <id>spigotmc-repo</id>
            <url>https://hub.spigotmc.org/nexus/content/repositories/snapshots/</url>
        </repository>
    </repositories>
    <dependencies>
        <dependency>
            <groupId>org.spigotmc</groupId>
            <artifactId>spigot-api</artifactId>
            <version>1.21.1-R0.1-SNAPSHOT</version>
            <scope>provided</scope>
        </dependency>
    </dependencies>
</project>";
    }

    public string GenerateDefaultPluginYml(string pluginName, string mainClass, Dictionary<string, string> files)
    {
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, fileContent) in files)
        {
            if (!path.EndsWith(".java")) continue;
            
            // 1. getCommand("xxx") çağrılarından komut adlarını çıkar
            var getCommandMatches = Regex.Matches(fileContent, @"getCommand\(\s*""(\w+)""\s*\)");
            foreach (Match m in getCommandMatches)
                commands.Add(m.Groups[1].Value.ToLower());
            
            // 2. XCommand sınıf adlarından komut adlarını çıkar
            var classMatches = Regex.Matches(fileContent, @"public\s+class\s+(\w+)Command\b");
            foreach (Match m in classMatches)
            {
                var cmdName = m.Groups[1].Value.ToLower();
                if (!string.IsNullOrEmpty(cmdName))
                    commands.Add(cmdName);
            }
            
            // 3. onCommand içindeki command.getName().equalsIgnoreCase("xxx") veya label.equalsIgnoreCase("xxx")
            var equalsMatches = Regex.Matches(fileContent, @"(?:command\.getName|label).*?equalsIgnoreCase\(\s*""(\w+)""\s*\)");
            foreach (Match m in equalsMatches)
                commands.Add(m.Groups[1].Value.ToLower());
            
            // 4. case "xxx": pattern (switch-case komut handling)
            if (fileContent.Contains("onCommand"))
            {
                var caseMatches = Regex.Matches(fileContent, @"case\s+""(\w+)"":");
                foreach (Match m in caseMatches)
                    commands.Add(m.Groups[1].Value.ToLower());
            }
        }

        var yml = $"name: {pluginName}\nversion: 1.0\nmain: {mainClass}\napi-version: '1.21'\ndescription: {pluginName} plugin";

        if (commands.Count > 0)
        {
            yml += "\ncommands:";
            foreach (var cmd in commands.OrderBy(c => c))
                yml += $"\n  {cmd}:\n    description: {cmd} komutu\n    usage: /{cmd}";
        }

        return yml;
    }

    /// <summary>
    /// Üretilen kodu derler ve plugin olarak deploy eder
    /// </summary>
    public async Task<BuildResult> BuildAndDeploy(string pluginName, Dictionary<string, string> files, string? sourcePluginDir = null)
    {
        // pluginName'den Project suffix'ini temizle
        var cleanName = pluginName.EndsWith("Project") ? pluginName[..^7] : pluginName;
        var pluginDir = Path.Combine(_serverPath, "plugins", $"{cleanName}Project");
        var result = new BuildResult { PluginName = cleanName };

        try
        {
            // Edit modunda kaynak dizini çözümle
            string? resolvedSourceDir = null;
            if (!string.IsNullOrEmpty(sourcePluginDir))
            {
                var candidates = new[] {
                    sourcePluginDir,
                    sourcePluginDir.EndsWith("Project") ? sourcePluginDir[..^7] : sourcePluginDir + "Project"
                };
                foreach (var c in candidates)
                {
                    var d = Path.Combine(_serverPath, "plugins", c);
                    if (Directory.Exists(d)) { resolvedSourceDir = d; break; }
                }
                _logger.LogInformation("Edit mode: resolved source dir = {Source}", resolvedSourceDir ?? "null");
            }

            // ÖNEMLİ: Silmeden ÖNCE mevcut pom.xml ve plugin.yml'ı kaydet
            var savedPom = "";
            var savedPluginYml = "";
            var savedPluginYmlPath = "";
            
            // Kaynak dizinden (veya pluginDir'den) pom.xml/plugin.yml'ı oku
            var checkDir = resolvedSourceDir ?? pluginDir;
            if (Directory.Exists(checkDir))
            {
                var pomPath = Path.Combine(checkDir, "pom.xml");
                if (File.Exists(pomPath))
                    savedPom = await File.ReadAllTextAsync(pomPath);
                
                var ymlFiles = Directory.GetFiles(checkDir, "plugin.yml", SearchOption.AllDirectories)
                    .Where(f => f.Contains("resources") && !f.Contains("target"));
                foreach (var yml in ymlFiles)
                {
                    savedPluginYml = await File.ReadAllTextAsync(yml);
                    savedPluginYmlPath = Path.GetRelativePath(checkDir, yml);
                    break;
                }
            }
            
            // Aynı dizin mi?
            bool selfEdit = resolvedSourceDir != null && 
                            Path.GetFullPath(resolvedSourceDir) == Path.GetFullPath(pluginDir);

            if (selfEdit)
            {
                // Kendi kendini düzenleme: sadece target/ temizle, dosyaları üzerine yaz
                var targetDir2 = Path.Combine(pluginDir, "target");
                if (Directory.Exists(targetDir2))
                    Directory.Delete(targetDir2, true);
                _logger.LogInformation("Edit mode: self-edit, keeping existing files, cleaning target/");
            }
            else
            {
                // Yeniden oluştur
                if (Directory.Exists(pluginDir))
                    Directory.Delete(pluginDir, true);
                
                // Farklı kaynak dizininden kopyala
                if (resolvedSourceDir != null && Directory.Exists(resolvedSourceDir))
                {
                    CopyDirectory(resolvedSourceDir, pluginDir);
                    var targetDir2 = Path.Combine(pluginDir, "target");
                    if (Directory.Exists(targetDir2))
                        Directory.Delete(targetDir2, true);
                }
            }

            // Kaydedilen pom.xml ve plugin.yml'ı geri yükle (eğer disk üzerinde yoksa)
            if (!string.IsNullOrEmpty(savedPom))
            {
                var pomDest = Path.Combine(pluginDir, "pom.xml");
                if (!File.Exists(pomDest))
                {
                    Directory.CreateDirectory(pluginDir);
                    await File.WriteAllTextAsync(pomDest, savedPom);
                    _logger.LogInformation("Restored saved pom.xml to {Path}", pomDest);
                }
            }
            if (!string.IsNullOrEmpty(savedPluginYml) && !string.IsNullOrEmpty(savedPluginYmlPath))
            {
                var ymlDest = Path.Combine(pluginDir, savedPluginYmlPath);
                if (!File.Exists(ymlDest))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ymlDest)!);
                    await File.WriteAllTextAsync(ymlDest, savedPluginYml);
                    _logger.LogInformation("Restored saved plugin.yml to {Path}", ymlDest);
                }
            }

            // Edit modunda: pom.xml ve plugin.yml'ı AI çıktısından ATLA (disk üzerindekiler korundu)
            // Ana sınıf koruması da uygula
            if (!string.IsNullOrEmpty(sourcePluginDir))
            {
                var filesToSkip = new List<string>();
                foreach (var (relativePath, content) in files)
                {
                    if (relativePath == "pom.xml" || relativePath.EndsWith("plugin.yml"))
                    {
                        var existingFile = Path.Combine(pluginDir, relativePath);
                        if (File.Exists(existingFile))
                        {
                            _logger.LogWarning("Edit mode: Skipping {File} — preserved from source", relativePath);
                            filesToSkip.Add(relativePath);
                        }
                        continue;
                    }
                    
                    if (!relativePath.EndsWith(".java")) continue;
                    var existingPath = Path.Combine(pluginDir, relativePath);
                    if (File.Exists(existingPath))
                    {
                        var existingContent = await File.ReadAllTextAsync(existingPath);
                        if (content.Contains("extends JavaPlugin"))
                        {
                            var origHasMarket = existingContent.Contains("MarketCommand") || existingContent.Contains("MarketGUI");
                            var aiHasMarket = content.Contains("MarketCommand") || content.Contains("MarketGUI");
                            var origHasAuction = existingContent.Contains("AcikArttirma") || existingContent.Contains("AuctionGUI");
                            var aiHasAuction = content.Contains("AcikArttirma") || content.Contains("AuctionGUI");
                            
                            if ((origHasMarket && !aiHasMarket) || (origHasAuction && !aiHasAuction) ||
                                content.Length < existingContent.Length * 0.5)
                            {
                                _logger.LogWarning("Edit mode: Skipping {File} — AI removed important code", relativePath);
                                await InjectNewListenerRegistration(existingPath, existingContent, content);
                                filesToSkip.Add(relativePath);
                                continue;
                            }
                        }
                    }
                }
                foreach (var skip in filesToSkip)
                    files.Remove(skip);
            }

            // İç sınıf import haritasını oluştur (edit modunda mevcut dosyalardan)
            var internalImportMap = BuildInternalImportMap(pluginDir);

            // AI dosyalarını yaz (üzerine yazar) — önce Java kodunu düzelt
            foreach (var (relativePath, content) in files)
            {
                var fullPath = Path.Combine(pluginDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                var fixedContent = relativePath.EndsWith(".java") 
                    ? FixJavaCode(content, relativePath, internalImportMap) 
                    : content;
                await File.WriteAllTextAsync(fullPath, fixedContent);
            }

            // Çapraz dosya hatalarını düzelt: private erişim, inner class çakışması vb.
            FixCrossFileAccess(pluginDir);

            // Maven ile derle
            var buildOutput = await RunProcess("mvn", "clean package", pluginDir);
            result.BuildOutput = buildOutput.Output;

            if (buildOutput.ExitCode != 0)
            {
                result.Success = false;
                // Maven hata satırlarını çıkar
                var errorLines = buildOutput.Output
                    .Split('\n')
                    .Where(l => l.Contains("[ERROR]") || l.Contains("error:") || l.Contains("cannot find symbol") || l.Contains("COMPILATION ERROR") || l.Contains("package") && l.Contains("does not exist"))
                    .Take(15)
                    .ToList();
                var errorSummary = errorLines.Count > 0 
                    ? string.Join("\n", errorLines) 
                    : buildOutput.Output.Length > 1500 ? buildOutput.Output[..1500] : buildOutput.Output;
                result.Error = $"Derleme hatası:\n{errorSummary}";
                return result;
            }

            // target/*.jar dosyasını bul ve plugins/ klasörüne kopyala
            var targetDir = Path.Combine(pluginDir, "target");
            var jarFile = Directory.GetFiles(targetDir, "*.jar")
                .FirstOrDefault(f => !f.Contains("original"));

            if (jarFile == null)
            {
                result.Success = false;
                result.Error = "JAR dosyası bulunamadı!";
                return result;
            }

            var destJar = Path.Combine(_serverPath, "plugins", Path.GetFileName(jarFile));
            
            // Aynı plugin'in eski JAR'larını temizle (farklı isimle kalmış olabilir)
            // plugin.yml'daki name'i oku ve eşleşen eski JAR'ları sil
            try
            {
                var builtPluginYml = Path.Combine(pluginDir, "target", "classes", "plugin.yml");
                if (File.Exists(builtPluginYml))
                {
                    var ymlContent = await File.ReadAllTextAsync(builtPluginYml);
                    var nameMatch = Regex.Match(ymlContent, @"^name:\s*(.+)$", RegexOptions.Multiline);
                    if (nameMatch.Success)
                    {
                        var pluginYmlName = nameMatch.Groups[1].Value.Trim();
                        // plugins/ klasöründeki tüm JAR'ları kontrol et
                        foreach (var existingJar in Directory.GetFiles(Path.Combine(_serverPath, "plugins"), "*.jar"))
                        {
                            if (existingJar == destJar) continue; // Kendi JAR'ını atlama
                            try
                            {
                                // JAR içindeki plugin.yml'ı oku
                                using var zip = System.IO.Compression.ZipFile.OpenRead(existingJar);
                                var entry = zip.GetEntry("plugin.yml");
                                if (entry != null)
                                {
                                    using var reader = new StreamReader(entry.Open());
                                    var existingYmlContent = await reader.ReadToEndAsync();
                                    var existingName = Regex.Match(existingYmlContent, @"^name:\s*(.+)$", RegexOptions.Multiline);
                                    if (existingName.Success && existingName.Groups[1].Value.Trim().Equals(pluginYmlName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        File.Delete(existingJar);
                                        _logger.LogWarning("Deleted old duplicate JAR: {Jar} (same plugin name: {Name})", Path.GetFileName(existingJar), pluginYmlName);
                                    }
                                }
                            }
                            catch { /* JAR okunamadıysa atla */ }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Old JAR cleanup failed, continuing...");
            }
            
            File.Copy(jarFile, destJar, true);

            result.Success = true;
            result.JarPath = destJar;
            result.Message = $"Plugin başarıyla derlendi ve deploy edildi: {Path.GetFileName(jarFile)}";

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Plugin build failed for {PluginName}", pluginName);
            return result;
        }
    }

    private async Task<AiResponse> CallOllama(string prompt, string? history)
    {
        var baseUrl = _config["AI:OllamaUrl"] ?? "http://localhost:11434";
        var model = _config["AI:OllamaModel"] ?? "spigot-codestral";

        // Custom model (spigot-codestral) zaten system prompt'u baked-in içerir
        var isCustomModel = model.StartsWith("spigot-");
        var systemPrompt = isCustomModel ? "" : GetSystemPrompt();
        
        var fullPrompt = string.IsNullOrEmpty(history) 
            ? $"Kullanıcı isteği: {prompt}" 
            : $"Önceki konuşma:\n{history}\n\nKullanıcı isteği: {prompt}";

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        
        // system prompt baked-in modeller için system göndermeye gerek yok, context window'u boşa harcamaz
        var requestBody = isCustomModel 
            ? new
            {
                model,
                prompt = fullPrompt,
                stream = false,
                options = new { temperature = 0.3, num_predict = 12000, num_ctx = 16384 }
            }
            : new
            {
                model,
                prompt = $"{systemPrompt}\n\n{fullPrompt}",
                stream = false,
                options = new { temperature = 0.3, num_predict = 12000, num_ctx = 16384 }
            };

        try
        {
            var response = await client.PostAsJsonAsync($"{baseUrl}/api/generate", requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("response").GetString() ?? "";

            return new AiResponse { Success = true, Content = text };
        }
        catch (Exception ex)
        {
            return new AiResponse { Success = false, Content = "", Error = $"Ollama bağlantı hatası: {ex.Message}" };
        }
    }

    private async Task<AiResponse> CallOpenAI(string prompt, string? history)
    {
        var apiKey = _config["AI:OpenAIKey"] ?? "";
        var model = _config["AI:OpenAIModel"] ?? "gpt-4o-mini";

        if (string.IsNullOrEmpty(apiKey))
            return new AiResponse { Success = false, Content = "", Error = "OpenAI API anahtarı ayarlanmamış!" };

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var messages = new List<object>
        {
            new { role = "system", content = GetSystemPrompt() }
        };
        if (!string.IsNullOrEmpty(history))
            messages.Add(new { role = "user", content = history });
        messages.Add(new { role = "user", content = prompt });

        var requestBody = new { model, messages, temperature = 0.7, max_tokens = 4096 };

        try
        {
            var response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            return new AiResponse { Success = true, Content = text };
        }
        catch (Exception ex)
        {
            return new AiResponse { Success = false, Content = "", Error = $"OpenAI hatası: {ex.Message}" };
        }
    }

    private async Task<AiResponse> CallAnthropic(string prompt, string? history)
    {
        var apiKey = _config["AI:AnthropicKey"] ?? "";
        var model = _config["AI:AnthropicModel"] ?? "claude-sonnet-4-20250514";

        if (string.IsNullOrEmpty(apiKey))
            return new AiResponse { Success = false, Content = "", Error = "Anthropic API anahtarı ayarlanmamış!" };

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var messages = new List<object>();
        if (!string.IsNullOrEmpty(history))
            messages.Add(new { role = "user", content = history });
        messages.Add(new { role = "user", content = prompt });

        var requestBody = new { model, messages, system = GetSystemPrompt(), max_tokens = 4096 };

        try
        {
            var response = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

            return new AiResponse { Success = true, Content = text };
        }
        catch (Exception ex)
        {
            return new AiResponse { Success = false, Content = "", Error = $"Anthropic hatası: {ex.Message}" };
        }
    }

    private async Task<AiResponse> CallOllamaRaw(string prompt, double temperature = 0.3, int maxTokens = 12000)
    {
        var baseUrl = _config["AI:OllamaUrl"] ?? "http://localhost:11434";
        var model = _config["AI:OllamaModel"] ?? "spigot-codestral";

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var requestBody = new
        {
            model,
            prompt,
            stream = false,
            options = new { temperature, num_predict = maxTokens, num_ctx = 16384 }
        };

        try
        {
            var response = await client.PostAsJsonAsync($"{baseUrl}/api/generate", requestBody);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("response").GetString() ?? "";
            return new AiResponse { Success = true, Content = text };
        }
        catch (Exception ex)
        {
            return new AiResponse { Success = false, Content = "", Error = $"Ollama hatası: {ex.Message}" };
        }
    }

    /// <summary>
    /// Mevcut dosyalardan yeni listener registration satırını enjekte eder
    /// </summary>
    private async Task InjectNewListenerRegistration(string mainClassPath, string existingContent, string aiContent)
    {
        // AI kodundan yeni registerEvents satırlarını bul
        var aiLines = aiContent.Split('\n');
        var existingLines = existingContent;
        var newRegistrations = new List<string>();
        var newImports = new List<string>();
        
        foreach (var line in aiLines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("registerEvents") && !existingLines.Contains(trimmed))
                newRegistrations.Add(line);
            if (trimmed.StartsWith("import ") && !existingLines.Contains(trimmed))
                newImports.Add(trimmed);
        }
        
        if (newRegistrations.Count == 0) return;
        
        var result = existingContent;
        
        // Import'ları ekle (son import satırından sonra)
        if (newImports.Count > 0)
        {
            var resultLines = result.Split('\n').ToList();
            var lastImportIdx = -1;
            for (int i = 0; i < resultLines.Count; i++)
                if (resultLines[i].TrimStart().StartsWith("import ")) lastImportIdx = i;
            if (lastImportIdx >= 0)
            {
                foreach (var imp in newImports)
                    resultLines.Insert(++lastImportIdx, imp);
                result = string.Join('\n', resultLines);
            }
        }
        
        // registerEvents satırlarını ekle (mevcut son registerEvents'den sonra)
        var lines2 = result.Split('\n').ToList();
        var lastRegIdx = -1;
        for (int i = 0; i < lines2.Count; i++)
            if (lines2[i].Contains("registerEvents")) lastRegIdx = i;
        if (lastRegIdx >= 0)
        {
            foreach (var reg in newRegistrations)
                lines2.Insert(++lastRegIdx, reg);
            result = string.Join('\n', lines2);
        }
        
        await File.WriteAllTextAsync(mainClassPath, result);
        _logger.LogInformation("Injected {Count} new listener registrations into main class", newRegistrations.Count);
    }

    /// <summary>
    /// Proje içindeki Java sınıflarından import haritası oluşturur
    /// </summary>
    private Dictionary<string, string> BuildInternalImportMap(string pluginDir)
    {
        var map = new Dictionary<string, string>();
        if (!Directory.Exists(pluginDir)) return map;
        
        var javaFiles = Directory.GetFiles(pluginDir, "*.java", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/target/"));
        
        foreach (var file in javaFiles)
        {
            var content = File.ReadAllText(file);
            // package bul
            var pkgMatch = System.Text.RegularExpressions.Regex.Match(content, @"package\s+([\w.]+);");
            if (!pkgMatch.Success) continue;
            var pkg = pkgMatch.Groups[1].Value;
            
            // sınıf adı bul
            var classMatch = System.Text.RegularExpressions.Regex.Match(content, @"public\s+(?:abstract\s+)?class\s+(\w+)");
            if (!classMatch.Success) continue;
            var className = classMatch.Groups[1].Value;
            
            map[className] = $"import {pkg}.{className};";
        }
        
        return map;
    }

    /// <summary>
    /// AI'ın ürettiği Java kodundaki yaygın hataları otomatik düzeltir
    /// </summary>
    private string FixJavaCode(string code, string relativePath, Dictionary<string, string>? internalImports = null)
    {
        var lines = code.Split('\n').ToList();
        var hasPackage = lines.Any(l => l.TrimStart().StartsWith("package "));
        var imports = new HashSet<string>();
        
        // 1. Dosya yolundan package bildirimi çıkar ve ekle
        if (!hasPackage && relativePath.Contains("src/main/java/"))
        {
            var pkgPath = relativePath
                .Replace("src/main/java/", "")
                .Replace("/", ".")
                .Replace(".java", "");
            var lastDot = pkgPath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var pkg = pkgPath[..lastDot];
                lines.Insert(0, $"package {pkg};\n");
            }
        }
        
        var joined = string.Join('\n', lines);
        
        // 2. Entity.getKiller() düzeltmesi — SADECE "Entity entity" varsa ve "LivingEntity" yoksa
        if (joined.Contains("entity.getKiller()"))
        {
            // Regex ile tam kelime eşleşmesi: "Entity entity" ama "LivingEntity entity" değil
            var entityPattern = new System.Text.RegularExpressions.Regex(@"(?<!Living)Entity\s+entity\s*=\s*event\.getEntity\(\)");
            if (entityPattern.IsMatch(joined))
            {
                joined = entityPattern.Replace(joined, "LivingEntity entity = event.getEntity()");
                if (!joined.Contains("import org.bukkit.entity.LivingEntity"))
                    imports.Add("import org.bukkit.entity.LivingEntity;");
            }
        }
        
        // 2b. PlayerDeathEvent yanlış paketten import edilmiş olabilir — düzelt
        // PlayerDeathEvent, org.bukkit.event.entity paketinde, org.bukkit.event.player DEĞİL
        joined = joined.Replace("import org.bukkit.event.player.PlayerDeathEvent;", "import org.bukkit.event.entity.PlayerDeathEvent;");
        
        // 3. Yaygın eksik Bukkit importlarını tespit et ve ekle
        var importMap = new Dictionary<string, string>
        {
            { "YamlConfiguration", "import org.bukkit.configuration.file.YamlConfiguration;" },
            { "new File(", "import java.io.File;" },
            { "IOException", "import java.io.IOException;" },
            { "ChatColor", "import org.bukkit.ChatColor;" },
            { "EntityType", "import org.bukkit.entity.EntityType;" },
            { "EntityDeathEvent", "import org.bukkit.event.entity.EntityDeathEvent;" },
            { "PlayerDeathEvent", "import org.bukkit.event.entity.PlayerDeathEvent;" },
            { "EventHandler", "import org.bukkit.event.EventHandler;" },
            { "HashMap<", "import java.util.HashMap;" },
            { "Map<", "import java.util.Map;" },
            { "UUID", "import java.util.UUID;" },
            { "ArrayList<", "import java.util.ArrayList;" },
            { "List<", "import java.util.List;" },
            { "ItemStack", "import org.bukkit.inventory.ItemStack;" },
            { "Material", "import org.bukkit.Material;" },
            { "Bukkit", "import org.bukkit.Bukkit;" },
            { "CommandSender", "import org.bukkit.command.CommandSender;" },
            { "Command ", "import org.bukkit.command.Command;" },
            { "BlockBreakEvent", "import org.bukkit.event.block.BlockBreakEvent;" },
            { "PlayerJoinEvent", "import org.bukkit.event.player.PlayerJoinEvent;" },
            { "PlayerQuitEvent", "import org.bukkit.event.player.PlayerQuitEvent;" },
            { "PlayerInteractEvent", "import org.bukkit.event.player.PlayerInteractEvent;" },
            { "PlayerMoveEvent", "import org.bukkit.event.player.PlayerMoveEvent;" },
            { "InventoryClickEvent", "import org.bukkit.event.inventory.InventoryClickEvent;" },
            { "Location", "import org.bukkit.Location;" },
            { "World", "import org.bukkit.World;" },
            { "BukkitRunnable", "import org.bukkit.scheduler.BukkitRunnable;" },
            { "FileConfiguration", "import org.bukkit.configuration.file.FileConfiguration;" },
        };
        
        // Import kontrolü: sadece sınıf kullanılıyor ama import yoksa ekle
        foreach (var (keyword, importLine) in importMap)
        {
            if (joined.Contains(keyword) && !joined.Contains(importLine))
                imports.Add(importLine);
        }
        
        // Listener/Player/LivingEntity importları (tam word match gerekli)
        if (System.Text.RegularExpressions.Regex.IsMatch(joined, @"\bPlayer\b") && !joined.Contains("import org.bukkit.entity.Player;"))
            imports.Add("import org.bukkit.entity.Player;");
        if (System.Text.RegularExpressions.Regex.IsMatch(joined, @"\bLivingEntity\b") && !joined.Contains("import org.bukkit.entity.LivingEntity;"))
            imports.Add("import org.bukkit.entity.LivingEntity;");
        if (System.Text.RegularExpressions.Regex.IsMatch(joined, @"implements\s+Listener\b") && !joined.Contains("import org.bukkit.event.Listener;"))
            imports.Add("import org.bukkit.event.Listener;");
        
        // 4. Proje içi sınıf importlarını ekle (BankManager, MarketManager vb.)
        if (internalImports != null)
        {
            foreach (var (className, importLine) in internalImports)
            {
                // Sınıf kendi dosyasında olabilir, onu atlama
                var ownClass = System.Text.RegularExpressions.Regex.Match(joined, @"public\s+class\s+(\w+)");
                if (ownClass.Success && ownClass.Groups[1].Value == className) continue;
                
                if (joined.Contains(className) && !joined.Contains(importLine))
                    imports.Add(importLine);
            }
        }
        
        // 5. Entity.EntityType → EntityType düzelt
        joined = joined.Replace("Entity.EntityType", "EntityType");
        
        // 5b. bankManager.deposit(player, ...) → bankManager.deposit(player.getUniqueId(), ...) düzelt
        var depositPattern = new System.Text.RegularExpressions.Regex(@"bankManager\.deposit\((\w+),");
        var depositMatch = depositPattern.Match(joined);
        if (depositMatch.Success)
        {
            var argName = depositMatch.Groups[1].Value;
            // Eğer argüman bir Player değişkeniyse ve .getUniqueId() yoksa
            if (!argName.Contains("getUniqueId") && !argName.Contains("uuid") && !argName.Contains("UUID"))
            {
                joined = depositPattern.Replace(joined, $"bankManager.deposit({argName}.getUniqueId(),");
            }
        }
        var withdrawPattern = new System.Text.RegularExpressions.Regex(@"bankManager\.withdraw\((\w+),");
        var withdrawMatch = withdrawPattern.Match(joined);
        if (withdrawMatch.Success)
        {
            var argName = withdrawMatch.Groups[1].Value;
            if (!argName.Contains("getUniqueId") && !argName.Contains("uuid") && !argName.Contains("UUID"))
            {
                joined = withdrawPattern.Replace(joined, $"bankManager.withdraw({argName}.getUniqueId(),");
            }
        }
        
        // 6. Importları dosyanın başına ekle (package satırından sonra)
        if (imports.Count > 0)
        {
            lines = joined.Split('\n').ToList();
            
            var packageIdx = lines.FindIndex(l => l.TrimStart().StartsWith("package "));
            var insertAt = packageIdx >= 0 ? packageIdx + 1 : 0;
            
            // Mevcut importlardan sonra ekle
            var lastImportIdx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("import "))
                    lastImportIdx = i;
            }
            if (lastImportIdx >= 0) insertAt = lastImportIdx + 1;
            
            joined = string.Join('\n', lines); // refresh joined
            foreach (var imp in imports.OrderBy(i => i))
            {
                if (!joined.Contains(imp))
                {
                    lines.Insert(insertAt, imp);
                    insertAt++;
                }
            }
            
            joined = string.Join('\n', lines);
        }
        
        return joined;
    }

    /// <summary>
    /// Tüm Java dosyalarını tarayarak çapraz dosya erişim hatalarını düzeltir:
    /// 1. private field'a başka dosyadan erişim → public yap
    /// 2. İç sınıf + aynı isimde dış dosya çakışması → iç sınıfı sil
    /// 3. Constructor parametresi eksikliği → düzelt
    /// </summary>
    private void FixCrossFileAccess(string pluginDir)
    {
        try
        {
            var javaFiles = Directory.GetFiles(pluginDir, "*.java", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/target/"))
                .ToDictionary(f => f, f => File.ReadAllText(f));

            if (javaFiles.Count <= 1) return;

            // 1. Her dosyadaki sınıf adlarını ve field'ları topla
            var classFields = new Dictionary<string, List<(string name, string accessModifier, string filePath)>>();
            var classFiles = new Dictionary<string, string>(); // className → filePath
            var outerClassNames = new HashSet<string>(); // ayrı dosya olarak var olan sınıf adları

            foreach (var (filePath, content) in javaFiles)
            {
                var classMatch = Regex.Match(content, @"public\s+class\s+(\w+)");
                if (!classMatch.Success) continue;
                var className = classMatch.Groups[1].Value;
                classFiles[className] = filePath;
                outerClassNames.Add(className);

                // Field'ları topla
                var fieldMatches = Regex.Matches(content, @"^\s*(private|protected|public)?\s+(?:static\s+)?(?:final\s+)?([\w<>,\s]+?)\s+(\w+)\s*[=;]", RegexOptions.Multiline);
                var fields = new List<(string name, string accessModifier, string filePath)>();
                foreach (Match fm in fieldMatches)
                {
                    var access = fm.Groups[1].Value;
                    var fieldName = fm.Groups[3].Value;
                    // Java keywords ve yaygın yanlış positifleri filtrele
                    if (fieldName is "class" or "return" or "new" or "if" or "else" or "void" or "null" or "true" or "false" or "this" or "super") continue;
                    fields.Add((fieldName, string.IsNullOrEmpty(access) ? "package" : access, filePath));
                }
                classFields[className] = fields;
            }

            // 2. İç sınıf + dış dosya çakışmasını düzelt
            foreach (var (filePath, content) in javaFiles.ToList())
            {
                var mainClassMatch = Regex.Match(content, @"public\s+class\s+(\w+)");
                if (!mainClassMatch.Success) continue;
                var mainClassName = mainClassMatch.Groups[1].Value;

                // Bu dosyada tanımlı private/public inner class'ları bul
                var innerClassPattern = new Regex(@"\n(\s*)(private|public|protected)?\s+class\s+(\w+)\s+implements\s+\w+[^{]*\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}", RegexOptions.Singleline);
                var innerMatches = innerClassPattern.Matches(content);
                var modified = content;
                var changed = false;

                foreach (Match im in innerMatches)
                {
                    var innerClassName = im.Groups[3].Value;
                    // Eğer aynı isimde ayrı bir .java dosyası varsa, iç sınıfı kaldır
                    if (outerClassNames.Contains(innerClassName) && classFiles.ContainsKey(innerClassName) && classFiles[innerClassName] != filePath)
                    {
                        _logger.LogWarning("Removing inner class {Inner} from {Main} — separate file exists", innerClassName, mainClassName);
                        modified = modified.Replace(im.Value, "");
                        changed = true;
                    }
                }

                if (changed)
                {
                    File.WriteAllText(filePath, modified);
                    javaFiles[filePath] = modified; // güncelle
                }
            }

            // 3. Başka dosyalardan erişilen private field'ları tespit et ve public yap
            foreach (var (className, fields) in classFields)
            {
                if (!classFiles.ContainsKey(className)) continue;
                var classFilePath = classFiles[className];
                var classContent = javaFiles[classFilePath];
                var fieldsToFix = new HashSet<string>();

                foreach (var (otherPath, otherContent) in javaFiles)
                {
                    if (otherPath == classFilePath) continue;
                    foreach (var (fieldName, access, _) in fields)
                    {
                        if (access != "private") continue;
                        // plugin.fieldName veya variable.fieldName pattern'i ara
                        if (Regex.IsMatch(otherContent, @"\b\w+\." + Regex.Escape(fieldName) + @"\b"))
                        {
                            fieldsToFix.Add(fieldName);
                        }
                    }
                }

                if (fieldsToFix.Count > 0)
                {
                    var fixedContent = classContent;
                    foreach (var fieldName in fieldsToFix)
                    {
                        // private [type] fieldName → public [type] fieldName
                        var fieldPattern = new Regex(@"(\s+)private(\s+(?:static\s+)?(?:final\s+)?[\w<>,\s]+?\s+" + Regex.Escape(fieldName) + @"\s*[=;])");
                        if (fieldPattern.IsMatch(fixedContent))
                        {
                            fixedContent = fieldPattern.Replace(fixedContent, "$1public$2", 1);
                            _logger.LogWarning("Changed {Field} in {Class} from private to public (accessed from other file)", fieldName, className);
                        }
                    }
                    if (fixedContent != classContent)
                    {
                        File.WriteAllText(classFilePath, fixedContent);
                        javaFiles[classFilePath] = fixedContent;
                    }
                }
            }

            // 4. Constructor'a plugin parametresi eksikse düzelt (new CommandHandler() → new CommandHandler(this))
            foreach (var (filePath, content) in javaFiles.ToList())
            {
                var modified = content;
                var changed = false;

                foreach (var (otherClassName, otherFilePath) in classFiles)
                {
                    if (otherFilePath == filePath) continue;
                    var otherContent = javaFiles[otherFilePath];

                    // Diğer dosyadaki sınıf, ana plugin'e referans yapıyorsa (plugin. kullanıyorsa)
                    // ve constructor'da parametre alıyorsa
                    var ctorMatch = Regex.Match(otherContent, @"public\s+" + Regex.Escape(otherClassName) + @"\s*\(\s*(\w+)\s+\w+\s*\)");
                    if (ctorMatch.Success)
                    {
                        // Bu dosyada new ClassName() (parametresiz) çağrısı var mı?
                        var noArgPattern = new Regex(@"new\s+" + Regex.Escape(otherClassName) + @"\s*\(\s*\)");
                        if (noArgPattern.IsMatch(modified))
                        {
                            modified = noArgPattern.Replace(modified, $"new {otherClassName}(this)");
                            changed = true;
                            _logger.LogWarning("Fixed constructor call: new {Class}() → new {Class}(this)", otherClassName, otherClassName);
                        }
                    }
                }

                if (changed)
                {
                    File.WriteAllText(filePath, modified);
                    javaFiles[filePath] = modified;
                }
            }

            // 5. Thread.sleep() inside event handlers → kaldır (sunucuyu dondurur)
            foreach (var (filePath, content) in javaFiles.ToList())
            {
                if (!content.Contains("Thread.sleep")) continue;
                if (!content.Contains("implements Listener")) continue;

                var modified = content;
                // while (...) { ... Thread.sleep ... } bloğunu kaldır
                var sleepBlockPattern = new Regex(
                    @"\s*//[^\n]*\n\s*long\s+startTime\s*=\s*System\.currentTimeMillis\(\);\s*\n\s*while\s*\([^)]*\)\s*\{[^}]*Thread\.sleep[^}]*\}\s*",
                    RegexOptions.Singleline);
                if (sleepBlockPattern.IsMatch(modified))
                {
                    modified = sleepBlockPattern.Replace(modified, "\n");
                    _logger.LogWarning("Removed blocking Thread.sleep loop from event handler in {File}", Path.GetFileName(filePath));
                }
                // Tek satır Thread.sleep'i de kaldır
                modified = Regex.Replace(modified, @"\s*try\s*\{\s*Thread\.sleep\([^)]*\);\s*\}\s*catch\s*\(InterruptedException\s+\w+\)\s*\{[^}]*\}\s*", "\n");

                if (modified != content)
                {
                    File.WriteAllText(filePath, modified);
                    javaFiles[filePath] = modified;
                }
            }

            // 6. Eksik kapanış parantezi düzelt (brace balancing)
            foreach (var (filePath, content) in javaFiles.ToList())
            {
                int openBraces = content.Count(c => c == '{');
                int closeBraces = content.Count(c => c == '}');
                if (openBraces > closeBraces)
                {
                    var missing = openBraces - closeBraces;
                    var fixedContent = content.TrimEnd() + "\n" + new string('}', missing) + "\n";
                    File.WriteAllText(filePath, fixedContent);
                    javaFiles[filePath] = fixedContent;
                    _logger.LogWarning("Added {Count} missing closing brace(s) to {File}", missing, Path.GetFileName(filePath));
                }
            }

            _logger.LogInformation("CrossFileAccess fix completed for {Dir}, {Count} files checked", Path.GetFileName(pluginDir), javaFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CrossFileAccess fix failed, continuing...");
        }
    }

    private void CleanupBuildDir(string dir) { }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    /// <summary>
    /// Mevcut plugin projelerini listeler
    /// </summary>
    public List<PluginInfo> GetExistingPlugins()
    {
        var plugins = new List<PluginInfo>();
        var pluginsDir = Path.Combine(_serverPath, "plugins");
        
        if (!Directory.Exists(pluginsDir)) return plugins;

        var allDirs = Directory.GetDirectories(pluginsDir).Select(Path.GetFileName).ToHashSet();

        // Kaynak kodlu projeleri bul (pom.xml ve src/ olan dizinler)
        foreach (var dir in Directory.GetDirectories(pluginsDir))
        {
            var dirName = Path.GetFileName(dir);
            var pomFile = Path.Combine(dir, "pom.xml");
            var srcDir = Path.Combine(dir, "src");
            
            // pom.xml ve src/ klasörü olmalı (sadece runtime config klasörlerini atla)
            if (!File.Exists(pomFile) || !Directory.Exists(srcDir)) continue;

            // Duplikat engelle: Eğer "XProject" varsa ve "X" de varsa, sadece "XProject"'i göster
            // Çünkü "X" Spigot'un oluşturduğu runtime config klasörü olabilir
            if (!dirName.EndsWith("Project"))
            {
                // "EconomyPlugin" ise ve "EconomyPluginProject" de varsa bunu atla
                if (allDirs.Contains(dirName + "Project"))
                    continue;
            }

            var info = new PluginInfo
            {
                Name = dirName,
                Path = dir,
                HasSource = true
            };

            // Java dosyalarını bul
            var javaFiles = Directory.GetFiles(dir, "*.java", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/target/"))
                .Select(f => Path.GetRelativePath(dir, f))
                .ToList();
            info.SourceFiles = javaFiles;

            // plugin.yml bul
            var pluginYml = Directory.GetFiles(dir, "plugin.yml", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.Contains("resources"));
            if (pluginYml != null) info.SourceFiles.Insert(0, Path.GetRelativePath(dir, pluginYml));
            
            // pom.xml ekle
            info.SourceFiles.Insert(0, "pom.xml");

            plugins.Add(info);
        }

        return plugins;
    }

    /// <summary>
    /// Bir plugin projesinin tüm kaynak kodlarını okur
    /// </summary>
    public Dictionary<string, string> ReadPluginSources(string pluginDirName)
    {
        var sources = new Dictionary<string, string>();
        var dir = Path.Combine(_serverPath, "plugins", pluginDirName);
        
        if (!Directory.Exists(dir)) return sources;

        // pom.xml
        var pomFile = Path.Combine(dir, "pom.xml");
        if (File.Exists(pomFile))
            sources["pom.xml"] = File.ReadAllText(pomFile);

        // plugin.yml
        var ymlFiles = Directory.GetFiles(dir, "plugin.yml", SearchOption.AllDirectories)
            .Where(f => f.Contains("resources") && !f.Contains("/target/"));
        foreach (var yml in ymlFiles)
            sources[Path.GetRelativePath(dir, yml)] = File.ReadAllText(yml);

        // Java dosyaları
        var javaFiles = Directory.GetFiles(dir, "*.java", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/target/"));
        foreach (var java in javaFiles)
            sources[Path.GetRelativePath(dir, java)] = File.ReadAllText(java);

        return sources;
    }

    private string GetSystemPrompt() => """
        Sen uzman bir Minecraft Spigot 1.21.1 plugin geliştiricisisin. Java 21 kullanıyorsun.
        SADECE derlenebilir, çalışan Java kodu üret. Minimum açıklama, MAXIMUM kod.

        ===== MUTLAK KURALLAR =====
        1. HER dosyada package bildirimi + TÜM import'lar olmalı!
        2. Her dosya: // FILE: src/main/java/com/example/pluginadi/Sinif.java başlığı + ```java bloku
        3. Yeni plugin: pom.xml + plugin.yml + Java sınıfları HEPSİ lazım!
        4. Düzenleme modu: SADECE değişen/yeni dosyalar. Değişmeyen dosyaları YAZMA!
        5. Kısaltma YASAK! "// ... diğer kodlar", "// existing code" gibi şeyler YASAK! TAM dosya yaz!
        6. Ana sınıfı (extends JavaPlugin) yazarken mevcut komut ve listener kayıtlarını SİLME!

        ===== ÇALIŞAN PLUGIN ÖRNEĞİ (BU FORMATI TAKİP ET!) =====
        Aşağıdaki plugin %100 derlenip çalışır. Yeni plugin yazarken bu yapıyı referans al:

        // FILE: pom.xml
        ```xml
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0">
            <modelVersion>4.0.0</modelVersion>
            <groupId>com.example</groupId>
            <artifactId>WelcomePlugin</artifactId>
            <version>1.0-SNAPSHOT</version>
            <packaging>jar</packaging>
            <properties>
                <maven.compiler.source>21</maven.compiler.source>
                <maven.compiler.target>21</maven.compiler.target>
                <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
            </properties>
            <repositories>
                <repository>
                    <id>spigot-repo</id>
                    <url>https://hub.spigotmc.org/nexus/content/repositories/snapshots/</url>
                </repository>
            </repositories>
            <dependencies>
                <dependency>
                    <groupId>org.spigotmc</groupId>
                    <artifactId>spigot-api</artifactId>
                    <version>1.21.1-R0.1-SNAPSHOT</version>
                    <scope>provided</scope>
                </dependency>
            </dependencies>
        </project>
        ```

        // FILE: src/main/resources/plugin.yml
        ```yaml
        name: WelcomePlugin
        version: '1.0'
        main: com.example.welcome.WelcomePlugin
        api-version: '1.21'
        commands:
          setwelcome:
            description: Karsilama mesajini ayarla
            usage: /setwelcome <mesaj>
            permission: welcome.set
        permissions:
          welcome.set:
            description: Karsilama mesaji ayarlama
            default: op
        ```

        // FILE: src/main/java/com/example/welcome/WelcomePlugin.java
        ```java
        package com.example.welcome;

        import org.bukkit.plugin.java.JavaPlugin;

        public class WelcomePlugin extends JavaPlugin {
            public String welcomeMessage = "Sunucuya hos geldin!";

            @Override
            public void onEnable() {
                getCommand("setwelcome").setExecutor(new WelcomeCommand(this));
                getServer().getPluginManager().registerEvents(new JoinListener(this), this);
                getLogger().info("WelcomePlugin aktif!");
            }
        }
        ```

        // FILE: src/main/java/com/example/welcome/WelcomeCommand.java
        ```java
        package com.example.welcome;

        import org.bukkit.command.Command;
        import org.bukkit.command.CommandExecutor;
        import org.bukkit.command.CommandSender;
        import org.bukkit.entity.Player;

        public class WelcomeCommand implements CommandExecutor {
            private final WelcomePlugin plugin;

            public WelcomeCommand(WelcomePlugin plugin) {
                this.plugin = plugin;
            }

            @Override
            public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
                if (!(sender instanceof Player player)) {
                    sender.sendMessage("Bu komut sadece oyuncular kullanabilir!");
                    return true;
                }
                if (args.length == 0) {
                    player.sendMessage("\u00a7cKullanim: /setwelcome <mesaj>");
                    return true;
                }
                plugin.welcomeMessage = String.join(" ", args);
                player.sendMessage("\u00a7aKarsilama mesaji ayarlandi: " + plugin.welcomeMessage);
                return true;
            }
        }
        ```

        // FILE: src/main/java/com/example/welcome/JoinListener.java
        ```java
        package com.example.welcome;

        import org.bukkit.entity.Player;
        import org.bukkit.event.EventHandler;
        import org.bukkit.event.Listener;
        import org.bukkit.event.player.PlayerJoinEvent;

        public class JoinListener implements Listener {
            private final WelcomePlugin plugin;

            public JoinListener(WelcomePlugin plugin) {
                this.plugin = plugin;
            }

            @EventHandler
            public void onPlayerJoin(PlayerJoinEvent event) {
                Player player = event.getPlayer();
                player.sendMessage("\u00a76" + plugin.welcomeMessage);
            }
        }
        ```
        ===== ÖRNEK BİTİŞ =====

        ===== SPİGOT API REFERANSİ =====
        EVENT'LER:
        - Mob ölümü: EntityDeathEvent → event.getEntity() LivingEntity döner, .getKiller() ile Player al
        - Oyuncu ölümü: PlayerDeathEvent (org.bukkit.event.entity paketi!)
        - Blok kırma: BlockBreakEvent
        - Oyuncu giriş: PlayerJoinEvent
        - Oyuncu çıkış: PlayerQuitEvent
        - Oyuncu hareket: PlayerMoveEvent
        - Hasar: EntityDamageByEntityEvent
        - Envanter tık: InventoryClickEvent
        - Sohbet: AsyncPlayerChatEvent
        - Etkileşim: PlayerInteractEvent

        SINIFLAR ve METOTLAR:
        - JavaPlugin: onEnable(), onDisable(), getConfig(), saveConfig(), getCommand(), getServer()
        - Player: sendMessage(), teleport(), getInventory(), getUniqueId(), getName(), getLocation()
        - CommandExecutor: onCommand(CommandSender, Command, String, String[]) → boolean
        - Listener: @EventHandler ile metot işaretle
        - BukkitRunnable: runTaskLater(plugin, ticks), runTaskTimer(plugin, delay, period)
        - Location: new Location(world, x, y, z), getWorld(), getX/Y/Z()
        - ItemStack: new ItemStack(Material.DIAMOND, amount)

        ===== ÇOK DOSYALI PLUGIN KURALLARI =====
        - Ayrı dosyadaki sınıfın erişeceği field'lar PUBLIC olmalı!
          ❌ private Map<UUID, Location> homes → DERLEME HATASI!
          ✅ public Map<UUID, Location> homes → Doğru!
        - Ayrı dosyadaki sınıfa plugin referansı:
          Constructor: public MyListener(MyPlugin plugin) { this.plugin = plugin; }
          Ana sınıfta: new MyListener(this)
        - Inner class + ayrı dosya aynı anda OLMAZ! Birini sil!

        ===== YASAKLI PATTERN'LER =====
        ❌ Thread.sleep() → Sunucu donar! ✅ BukkitRunnable.runTaskLater(plugin, 20L)
        ❌ Entity entity = event.getEntity(); entity.getKiller() → DERLEME HATASI!
        ❌ Entity.EntityType → YANLIŞ! ✅ EntityType.ZOMBIE
        ❌ PlayerKillEntityEvent → YOKTUR!
        ❌ import org.bukkit.event.player.PlayerDeathEvent → YANLIŞ paket! ✅ org.bukkit.event.entity.PlayerDeathEvent

        ===== EKONOMİ SİSTEMİ =====
        Sunucuda EconomyPlugin var. BankManager API:
        - deposit(UUID, double), withdraw(UUID, double) → boolean, getBalance(UUID) → double
        - DOĞRU: bankManager.deposit(player.getUniqueId(), amount)
        - YANLIŞ: bankManager.deposit(player, amount) → Player kabul ETMEZ!

        ===== MAVEN AYARLARI =====
        - groupId: com.example, api-version: '1.21'
        - spigot-api: 1.21.1-R0.1-SNAPSHOT (scope: provided)
        - Repo: https://hub.spigotmc.org/nexus/content/repositories/snapshots/
        - Java: source 21, target 21

        Türkçe yanıt ver. Minimum açıklama + TAM, DERLENEBİLİR KOD.
        """;

    private async Task<ProcessResult> RunProcess(string command, string args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["JAVA_HOME"] = _javaHome;

        var sb = new StringBuilder();
        using var proc = Process.Start(psi)!;
        
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        
        await proc.WaitForExitAsync();
        
        sb.AppendLine(await outTask);
        sb.AppendLine(await errTask);

        return new ProcessResult { ExitCode = proc.ExitCode, Output = sb.ToString() };
    }
}

public class AiResponse
{
    public bool Success { get; set; }
    public string Content { get; set; } = "";
    public string? Error { get; set; }
}

public class BuildResult
{
    public bool Success { get; set; }
    public string PluginName { get; set; } = "";
    public string? JarPath { get; set; }
    public string? BuildOutput { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
}

public class PluginInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool HasSource { get; set; }
    public List<string> SourceFiles { get; set; } = new();
}
