using System;

namespace AutoDuty.Windows;

[Serializable]
public sealed class PlannerItem
{
    public uint TerritoryType;
    public int TargetRuns = 1;
    public int CompletedRuns;
    public string? PathFileName;
}
