using System;
using System.Linq;
using AutoDuty.Helpers;
using AutoDuty.Windows;
using ECommons.DalamudServices;

namespace AutoDuty;

public sealed partial class AutoDuty
{
    private DateTime _plannerLastDutyCompletedAtUtc = DateTime.MinValue;
    private uint _plannerLastDutyCompletedTerritoryType;

    internal PlannerItem? CurrentPlannerItem
        => Configuration.PlannerItems.Count == 0
            ? null
            : Configuration.PlannerItems[Math.Clamp(Configuration.PlannerCurrentIndex, 0, Configuration.PlannerItems.Count - 1)];

    internal bool TryStartPlanner(out string error)
    {
        error = string.Empty;
        if (States.HasFlag(PluginState.Looping) || States.HasFlag(PluginState.Navigating))
        {
            error = "AutoDuty 正在執行中，請先停止目前工作。";
            return false;
        }
        if (!Configuration.PlannerEnabled)
        {
            error = "請先啟用排程器。";
            return false;
        }
        if (Configuration.PlannerItems.Count == 0)
        {
            error = "排程清單為空。";
            return false;
        }
        var plannerMode = Configuration.PlannerDutyMode;
        if (plannerMode == DutyMode.None)
        {
            error = "請先選擇副本模式。";
            return false;
        }
        if (LevelingEnabled)
        {
            error = "排程器與自動練等不能同時執行，請先將練等模式設為無。";
            return false;
        }
        if (Svc.Party.PartyId > 0 && plannerMode is DutyMode.Support or DutyMode.Squadron or DutyMode.Trust)
        {
            error = "任務支援器、冒險者分隊及親信戰友模式不可在組隊狀態下執行。";
            return false;
        }
        if (plannerMode == DutyMode.Regular && !Configuration.Unsynced && !Configuration.OverridePartyValidation && Svc.Party.PartyId == 0)
        {
            error = "同步的一般副本模式需要四人小隊。";
            return false;
        }
        if (plannerMode == DutyMode.Regular && !Configuration.Unsynced && !Configuration.OverridePartyValidation && !ObjectHelper.PartyValidation())
        {
            error = "同步的一般副本模式需要正確的隊伍職業配置。";
            return false;
        }

        NormalizePlannerProgress();
        if (Configuration.PlannerItems.All(IsPlannerItemComplete))
        {
            if (!Configuration.PlannerRepeat)
            {
                error = "排程已完成；請重設進度或啟用循環執行。";
                return false;
            }
            ResetPlannerProgress(save: false);
        }

        var nextIndex = FindNextIncompletePlannerIndex(Configuration.PlannerCurrentIndex);
        if (nextIndex < 0)
        {
            error = "找不到尚未完成的排程項目。";
            return false;
        }

        for (var i = 0; i < Configuration.PlannerItems.Count; i++)
        {
            var item = Configuration.PlannerItems[i];
            if (!ContentHelper.DictionaryContent.TryGetValue(item.TerritoryType, out var content))
            {
                error = $"第 {i + 1} 項的副本（{item.TerritoryType}）不存在。";
                return false;
            }
            if (!content.DutyModes.HasFlag(plannerMode))
            {
                error = $"第 {i + 1} 項「{content.Name}」不支援目前的副本模式。";
                return false;
            }
            if (!content.CanRun(mode: plannerMode))
            {
                error = $"第 {i + 1} 項「{content.Name}」目前不可執行；請檢查解鎖、等級與裝等。";
                return false;
            }
            if (!ContentPathsManager.DictionaryPaths.TryGetValue(item.TerritoryType, out var paths) || paths.Paths.Count == 0)
            {
                error = $"第 {i + 1} 項「{content.Name}」沒有可用路徑。";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(item.PathFileName) && !paths.Paths.Any(path => path.FileName.Equals(item.PathFileName, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"第 {i + 1} 項「{content.Name}」指定的路徑已不存在。";
                return false;
            }
        }

        Configuration.AutoDutyModeEnum = AutoDutyMode.Looping;
        Configuration.DutyModeEnum = plannerMode;
        Configuration.PlannerCurrentIndex = nextIndex;
        PlannerFinished = false;
        PlannerRunning = true;
        _plannerLastDutyCompletedAtUtc = DateTime.MinValue;
        _plannerLastDutyCompletedTerritoryType = 0;

        if (!TryApplyPlannerSelection(out error))
        {
            PlannerRunning = false;
            return false;
        }

        Configuration.Save();
        Run(0, 0, startFromZero: true, bareMode: false);
        if (CurrentPlannerItem is { } startingItem)
            CurrentLoop = Math.Clamp(startingItem.CompletedRuns + 1, 1, startingItem.TargetRuns);
        return true;
    }

    internal void ResetPlannerProgress() => ResetPlannerProgress(save: true);

    private void ResetPlannerProgress(bool save)
    {
        foreach (var item in Configuration.PlannerItems)
            item.CompletedRuns = 0;

        Configuration.PlannerCurrentIndex = 0;
        PlannerFinished = false;
        if (save)
            Configuration.Save();
    }

    private void NormalizePlannerProgress()
    {
        foreach (var item in Configuration.PlannerItems)
        {
            item.TargetRuns = Math.Max(1, item.TargetRuns);
            item.CompletedRuns = Math.Clamp(item.CompletedRuns, 0, item.TargetRuns);
        }
        Configuration.PlannerCurrentIndex = Configuration.PlannerItems.Count == 0
            ? 0
            : Math.Clamp(Configuration.PlannerCurrentIndex, 0, Configuration.PlannerItems.Count - 1);
    }

    private static bool IsPlannerItemComplete(PlannerItem item)
        => item.CompletedRuns >= Math.Max(1, item.TargetRuns);

    private int FindNextIncompletePlannerIndex(int startIndex)
    {
        if (Configuration.PlannerItems.Count == 0)
            return -1;

        startIndex = Math.Clamp(startIndex, 0, Configuration.PlannerItems.Count - 1);
        for (var i = startIndex; i < Configuration.PlannerItems.Count; i++)
            if (!IsPlannerItemComplete(Configuration.PlannerItems[i]))
                return i;
        for (var i = 0; i < startIndex; i++)
            if (!IsPlannerItemComplete(Configuration.PlannerItems[i]))
                return i;
        return -1;
    }

    private bool TryApplyPlannerSelection(out string error)
    {
        error = string.Empty;
        if (!PlannerRunning)
            return true;

        NormalizePlannerProgress();
        var item = CurrentPlannerItem;
        if (item == null)
        {
            error = "目前沒有排程項目。";
            return false;
        }
        if (!ContentHelper.DictionaryContent.TryGetValue(item.TerritoryType, out var content))
        {
            error = $"找不到排程副本（{item.TerritoryType}）。";
            return false;
        }
        if (!ContentPathsManager.DictionaryPaths.TryGetValue(item.TerritoryType, out var paths) || paths.Paths.Count == 0)
        {
            error = $"「{content.Name}」沒有可用路徑。";
            return false;
        }

        CurrentTerritoryContent = content;
        if (!TrySelectPlannerPath(paths, out CurrentPath))
            paths.SelectPath(out CurrentPath);
        return CurrentPath >= 0;
    }

    private bool TrySelectPlannerPath(ContentPathsManager.ContentPathContainer paths, out int pathIndex)
    {
        pathIndex = -1;
        var item = CurrentPlannerItem;
        if (!PlannerRunning || item == null || item.TerritoryType != paths.Content.TerritoryType || string.IsNullOrWhiteSpace(item.PathFileName))
            return false;

        pathIndex = paths.Paths.FindIndex(path => path.FileName.Equals(item.PathFileName, StringComparison.OrdinalIgnoreCase));
        return pathIndex >= 0;
    }

    private int GetEffectiveLoopTimes()
    {
        if (PlannerRunning && CurrentPlannerItem is { } item)
            return Math.Max(1, item.TargetRuns);
        return Math.Max(1, Configuration.LoopTimes);
    }

    private void PlannerOnDutyCompleted()
    {
        if (!PlannerRunning || PlannerFinished || CurrentPlannerItem is not { } item)
            return;
        if (Svc.ClientState.TerritoryType != item.TerritoryType)
        {
            Svc.Log.Warning($"排程器忽略不相符的完成事件：目前區域 {Svc.ClientState.TerritoryType}，排程項目 {item.TerritoryType}");
            return;
        }

        var now = DateTime.UtcNow;
        if (_plannerLastDutyCompletedTerritoryType == item.TerritoryType && (now - _plannerLastDutyCompletedAtUtc).TotalSeconds < 2)
        {
            Svc.Log.Debug("排程器忽略重複的副本完成事件。");
            return;
        }
        _plannerLastDutyCompletedTerritoryType = item.TerritoryType;
        _plannerLastDutyCompletedAtUtc = now;

        item.TargetRuns = Math.Max(1, item.TargetRuns);
        item.CompletedRuns = Math.Clamp(item.CompletedRuns + 1, 0, item.TargetRuns);
        if (!IsPlannerItemComplete(item))
        {
            Configuration.Save();
            return;
        }

        var nextIndex = FindNextIncompletePlannerIndex(Configuration.PlannerCurrentIndex + 1);
        if (nextIndex >= 0)
        {
            Configuration.PlannerCurrentIndex = nextIndex;
            CurrentLoop = 0;
            if (!TryApplyPlannerSelection(out var error))
            {
                MainWindow.ShowPopup("排程器", error);
                PlannerFinished = true;
            }
        }
        else if (Configuration.PlannerRepeat)
        {
            ResetPlannerProgress(save: false);
            CurrentLoop = 0;
            if (!TryApplyPlannerSelection(out var error))
            {
                MainWindow.ShowPopup("排程器", error);
                PlannerFinished = true;
            }
            else
            {
                Svc.Log.Info("排程器完成一輪，從第一項重新開始。");
            }
        }
        else
        {
            PlannerFinished = true;
            MainWindow.ShowPopup("排程器", "排程已完成。");
            Svc.Log.Info("排程器已完成全部項目。");
        }
        Configuration.Save();
    }
}
