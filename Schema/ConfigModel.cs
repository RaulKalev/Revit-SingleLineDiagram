using System.Collections.Generic;

public class ConfigModel
{
    public bool IsDarkMode { get; set; } = true;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }


    // Project → ViewName → Stage (PP/TP) → Settings
    public Dictionary<string, Dictionary<string, Dictionary<string, ProjectConfig>>> Projects { get; set; }
        = new Dictionary<string, Dictionary<string, Dictionary<string, ProjectConfig>>>();
}

public class ProjectConfig
{
    public List<string> SelectedLevels { get; set; } = new List<string>();

    public string SelectedTagName { get; set; }

    public double TagOffsetMm { get; set; }
    // SubsystemKey → Selection + Section state
    public Dictionary<string, SubsystemConfig> Subsystems { get; set; } = new Dictionary<string, SubsystemConfig>();

    public double StartOffsetMm { get; set; } = 10;
    public double RightOffsetMm { get; set; } = 10; // default value in mm
    public string TagParameterName { get; set; } = "Comments";
    public double SpacingMm { get; set; } = 500;
    public int SectionCount { get; set; } = 1;
    public double MarginMm { get; set; } = 10; // default value in mm
    public double VerticalMarginMm { get; set; } = 10.0;
    public double ControllerOffsetMm { get; set; } = 150;
    public string ControllerDetailTypeId { get; set; }
    public int ItemsInRow { get; set; } = 20;
    public double RowOffset { get; set; } = 500;
}
public class SubsystemConfig
{
    public Dictionary<string, bool> SystemSelection { get; set; } = new Dictionary<string, bool>();
    public Dictionary<string, int> SystemSections { get; set; } = new Dictionary<string, int>();

}

