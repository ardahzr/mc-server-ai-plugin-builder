namespace McPanel.Models;

public class ProjectCreateRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ClarificationQuestion
{
    public int Id { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Placeholder { get; set; } = "Cevabınızı yazın...";
}

public class ClarificationRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ClarificationQuestion> Answers { get; set; } = new();
}

public class PluginBlueprint
{
    public string PluginName { get; set; } = "";
    public string Description { get; set; } = "";
    public GeneralInfo General { get; set; } = new();
    public List<CommandInfo> Commands { get; set; } = new();
    public List<PermissionInfo> Permissions { get; set; } = new();
    public List<UserStory> UserStories { get; set; } = new();
    public TechnicalDetails Technical { get; set; } = new();
    public string RawBlueprintJson { get; set; } = "";
}

public class GeneralInfo
{
    public string Summary { get; set; } = "";
    public string DetailedDescription { get; set; } = "";
    public List<string> Features { get; set; } = new();
}

public class CommandInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Usage { get; set; } = "";
    public string Permission { get; set; } = "";
}

public class PermissionInfo
{
    public string Node { get; set; } = "";
    public string Description { get; set; } = "";
    public string Default { get; set; } = "op";
}

public class UserStory
{
    public string Role { get; set; } = "PLAYER";
    public string Story { get; set; } = "";
}

public class TechnicalDetails
{
    public List<EventListenerInfo> EventListeners { get; set; } = new();
    public List<ClassInfo> Classes { get; set; } = new();
    public List<ApiMethodInfo> ApiMethods { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
}

public class EventListenerInfo
{
    public string EventName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Verified { get; set; } = true;
}

public class ClassInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

public class ApiMethodInfo
{
    public string Signature { get; set; } = "";
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Verified { get; set; } = true;
}

public class GenerateRequest
{
    public string PluginName { get; set; } = "";
    public string BlueprintJson { get; set; } = "";
}
