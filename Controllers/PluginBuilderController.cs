using Microsoft.AspNetCore.Mvc;
using McPanel.Services;
using McPanel.Models;
using System.Text.RegularExpressions;

namespace McPanel.Controllers;

public class PluginBuilderController : Controller
{
    private readonly PluginBuilderService _builder;
    private readonly ILogger<PluginBuilderController> _logger;

    public PluginBuilderController(PluginBuilderService builder, ILogger<PluginBuilderController> logger)
    {
        _builder = builder;
        _logger = logger;
    }

    public IActionResult Index()
    {
        ViewBag.ExistingPlugins = _builder.GetExistingPlugins();
        return View();
    }

    [HttpGet]
    public IActionResult ListPlugins()
    {
        return Json(_builder.GetExistingPlugins());
    }

    [HttpGet]
    public IActionResult GetPluginSource(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Json(new { success = false, error = "Plugin adi gerekli!" });
        var sources = _builder.ReadPluginSources(name);
        if (sources.Count == 0)
            return Json(new { success = false, error = "Plugin bulunamadi!" });
        return Json(new { success = true, files = sources });
    }

    // ========== WIZARD ENDPOINTS ==========

    [HttpPost]
    public async Task<IActionResult> GetQuestions([FromBody] ProjectCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return Json(new { success = false, error = "Plugin aciklamasi gerekli!" });
        try
        {
            var questions = await _builder.GenerateClarificationQuestions(request.Description);
            return Json(new { success = true, questions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate questions");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GenerateBlueprint([FromBody] ClarificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return Json(new { success = false, error = "Aciklama gerekli!" });
        try
        {
            var blueprint = await _builder.GenerateBlueprint(request.Name, request.Description, request.Answers);
            return Json(new { success = true, blueprint });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate blueprint");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GenerateAndBuild([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PluginName) || string.IsNullOrWhiteSpace(request.BlueprintJson))
            return Json(new { success = false, error = "Plugin adi ve blueprint gerekli!" });
        try
        {
            var codeResponse = await _builder.GenerateCodeFromBlueprint(request.PluginName, request.BlueprintJson);
            if (!codeResponse.Success)
                return Json(new { success = false, error = codeResponse.Error, phase = "generate" });

            var files = ParseFilesFromCode(codeResponse.Content, request.PluginName);
            if (files.Count == 0)
                return Json(new { success = false, error = "Koddan dosya cikarilamadi!", phase = "parse", code = codeResponse.Content });

            EnsurePomAndYml(files, request.PluginName);

            var buildResult = await _builder.BuildAndDeploy(request.PluginName, files);
            if (buildResult.Success)
                return Json(new { success = true, message = buildResult.Message, code = codeResponse.Content, jarPath = buildResult.JarPath });

            // Auto-retry up to 5 times
            var retryCode = codeResponse.Content;
            for (int i = 0; i < 5; i++)
            {
                var fixResponse = await _builder.FixCompilationErrors(retryCode, buildResult.Error ?? "Unknown error", request.PluginName);
                if (!fixResponse.Success) break;
                retryCode = fixResponse.Content;
                var retryFiles = ParseFilesFromCode(retryCode, request.PluginName);
                if (retryFiles.Count == 0) break;
                EnsurePomAndYml(retryFiles, request.PluginName);
                buildResult = await _builder.BuildAndDeploy(request.PluginName, retryFiles);
                if (buildResult.Success)
                    return Json(new { success = true, message = $"{buildResult.Message} (otomatik duzeltme, deneme {i + 1})", code = retryCode, jarPath = buildResult.JarPath, retries = i + 1 });
            }
            return Json(new { success = false, error = buildResult.Error, code = retryCode, buildOutput = buildResult.BuildOutput, phase = "build" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateAndBuild failed");
            return Json(new { success = false, error = ex.Message, phase = "exception" });
        }
    }

    // ========== LEGACY EDIT MODE ==========

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Json(new { success = false, error = "Mesaj bos olamaz!" });
        var response = await _builder.GeneratePluginCode(request.Message, request.History);
        if (!response.Success)
            return Json(new { success = false, error = response.Error });
        return Json(new { success = true, content = response.Content });
    }

    [HttpPost]
    public async Task<IActionResult> Build([FromBody] BuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PluginName) || request.Code == null)
            return Json(new { success = false, error = "Plugin adi ve kod gerekli!" });
        try
        {
            var files = ParseFilesFromCode(request.Code, request.PluginName);
            if (files.Count == 0)
                return Json(new { success = false, error = "Koddan dosya cikarilamadi!" });

            // Edit modunda da pom.xml ve plugin.yml'in var oldugundan emin ol
            // BuildAndDeploy icinde source kopyalanir ama source'ta da yoksa sorun olur
            EnsurePomAndYml(files, request.PluginName);

            // Edit modundaysa, mevcut plugin dizininde pom.xml yoksa oraya da yaz
            if (!string.IsNullOrEmpty(request.SourcePlugin))
            {
                var sourceDir = Path.Combine(_builder.ServerPath, "plugins", request.SourcePlugin);
                var sourcePom = Path.Combine(sourceDir, "pom.xml");
                if (Directory.Exists(sourceDir) && !System.IO.File.Exists(sourcePom) && files.ContainsKey("pom.xml"))
                {
                    System.IO.File.WriteAllText(sourcePom, files["pom.xml"]);
                    _logger.LogInformation("Edit mode: pom.xml yazildi -> {Path}", sourcePom);
                }
                var sourceYml = Path.Combine(sourceDir, "src", "main", "resources", "plugin.yml");
                if (Directory.Exists(sourceDir) && !System.IO.File.Exists(sourceYml) && files.Any(f => f.Key.EndsWith("plugin.yml")))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(sourceYml)!);
                    System.IO.File.WriteAllText(sourceYml, files.First(f => f.Key.EndsWith("plugin.yml")).Value);
                    _logger.LogInformation("Edit mode: plugin.yml yazildi -> {Path}", sourceYml);
                }
            }

            var result = await _builder.BuildAndDeploy(request.PluginName, files, request.SourcePlugin);
            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build failed for {Plugin}", request.PluginName);
            return Json(new { success = false, error = ex.Message });
        }
    }

    // ========== HELPERS ==========

    private void EnsurePomAndYml(Dictionary<string, string> files, string pluginName)
    {
        if (!files.ContainsKey("pom.xml"))
            files["pom.xml"] = _builder.GenerateDefaultPom(pluginName);
        if (!files.Any(f => f.Key.EndsWith("plugin.yml")))
        {
            var mainClass = DetectMainClass(files, pluginName);
            files["src/main/resources/plugin.yml"] = _builder.GenerateDefaultPluginYml(pluginName, mainClass, files);
        }
    }

    private string DetectMainClass(Dictionary<string, string> files, string pluginName)
    {
        foreach (var (path, content) in files)
        {
            if (!path.EndsWith(".java")) continue;
            var m = Regex.Match(content, @"public\s+class\s+(\w+)\s+extends\s+JavaPlugin");
            if (m.Success)
            {
                var cn = m.Groups[1].Value;
                var pm = Regex.Match(content, @"package\s+([\w.]+);");
                var pkg = pm.Success ? pm.Groups[1].Value : "com.example.plugin";
                return $"{pkg}.{cn}";
            }
        }
        return $"com.example.{pluginName.ToLower()}.Main";
    }

    private Dictionary<string, string> ParseFilesFromCode(string code, string pluginName)
    {
        var files = new Dictionary<string, string>();
        var filePattern = @"//\s*FILE:\s*(.+?)\s*\n```(?:java|xml|ya?ml)\s*\n([\s\S]*?)```";
        var matches = Regex.Matches(code, filePattern);

        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Trim();
                var content = match.Groups[2].Value.Trim();
                files[path] = content;
            }
        }
        else
        {
            var codeBlocks = Regex.Matches(code, @"```(java|xml|ya?ml)\s*\n([\s\S]*?)```");
            string? pomContent = null;
            string? pluginYml = null;
            var javaFiles = new List<(string path, string content)>();

            foreach (Match block in codeBlocks)
            {
                var lang = block.Groups[1].Value;
                var content = block.Groups[2].Value.Trim();
                if (lang == "xml" && content.Contains("<project")) { pomContent = content; }
                else if ((lang == "yml" || lang == "yaml") && content.Contains("main:")) { pluginYml = content; }
                else if (lang == "java")
                {
                    var classMatch = Regex.Match(content, @"public\s+class\s+(\w+)");
                    if (classMatch.Success)
                    {
                        var className = classMatch.Groups[1].Value;
                        var pkgMatch = Regex.Match(content, @"package\s+([\w.]+);");
                        var pkg = pkgMatch.Success ? pkgMatch.Groups[1].Value : $"com.example.{pluginName.ToLower()}";
                        var pkgPath = pkg.Replace('.', '/');
                        javaFiles.Add(($"src/main/java/{pkgPath}/{className}.java", content));
                    }
                }
            }
            if (pomContent != null) files["pom.xml"] = pomContent;
            if (pluginYml != null) files["src/main/resources/plugin.yml"] = pluginYml;
            foreach (var (path, content) in javaFiles) files[path] = content;
        }
        return files;
    }
}

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? History { get; set; }
}

public class BuildRequest
{
    public string PluginName { get; set; } = "";
    public string Code { get; set; } = "";
    public string? SourcePlugin { get; set; }
}
