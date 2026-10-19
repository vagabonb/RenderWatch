namespace RenderWatch.Core.Models;

public class RenderProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> MainProcessNames { get; set; } = new();
    public List<string> AssociatedProcessNames { get; set; } = new();
    public string? WindowTitleFilter { get; set; }
    public string? LogFilePathPattern { get; set; }
    public List<string> LogCompletionPatterns { get; set; } = new();
    public bool IsBuiltIn { get; set; }
    public double CpuThresholdPercent { get; set; } = 15.0;
    public double GpuThresholdPercent { get; set; } = 15.0;

    public static List<RenderProfile> GetDefaultProfiles() => new()
    {
        new RenderProfile
        {
            Id = "auto",
            Name = "Автоопределение",
            Description = "Автоматический поиск активных рендер-процессов (Blender, 3ds Max, V-Ray, Revit, Corona)",
            MainProcessNames = new() { "blender", "blender-app", "3dsmax", "revit", "cinema 4d", "cinema4d", "maya" },
            AssociatedProcessNames = new() { "vray", "vrayneomodel", "vrayrt", "cycles", "coronarenderer", "arnold" },
            IsBuiltIn = true
        },
        new RenderProfile
        {
            Id = "blender",
            Name = "Blender",
            Description = "Мониторинг Blender 3D (Cycles / EEVEE)",
            MainProcessNames = new() { "blender", "blender-app" },
            AssociatedProcessNames = new() { "cycles" },
            WindowTitleFilter = "Blender",
            IsBuiltIn = true
        },
        new RenderProfile
        {
            Id = "3dsmax_vray",
            Name = "3ds Max / V-Ray",
            Description = "Autodesk 3ds Max с рендерами V-Ray или Corona",
            MainProcessNames = new() { "3dsmax" },
            AssociatedProcessNames = new() { "vray", "vrayneomodel", "vrayrt", "coronarenderer" },
            WindowTitleFilter = "3ds Max",
            IsBuiltIn = true
        },
        new RenderProfile
        {
            Id = "vray_standalone",
            Name = "V-Ray Standalone",
            Description = "Автономный рендер V-Ray Standalone / Distributed",
            MainProcessNames = new() { "vray", "vrayneomodel" },
            AssociatedProcessNames = new() { "vrayrt" },
            IsBuiltIn = true
        },
        new RenderProfile
        {
            Id = "revit",
            Name = "Autodesk Revit",
            Description = "Autodesk Revit визуализация и экспорт",
            MainProcessNames = new() { "revit" },
            AssociatedProcessNames = new(),
            WindowTitleFilter = "Autodesk Revit",
            IsBuiltIn = true
        }
    };
}