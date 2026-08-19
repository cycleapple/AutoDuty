using System;
using System.Linq;
using AutoDuty.Helpers;
using Dalamud.Interface.Components;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;

namespace AutoDuty.Windows;

public static class PlannerTab
{
    private static string _searchText = string.Empty;
    private static int _newItemRuns = 1;

    private static readonly DutyMode[] SupportedModes =
    [
        DutyMode.Support,
        DutyMode.Trust,
        DutyMode.Squadron,
        DutyMode.Regular,
        DutyMode.Trial,
        DutyMode.Raid,
        DutyMode.Variant,
    ];

    private static string DutyModeName(DutyMode mode) => mode switch
    {
        DutyMode.None => "無",
        DutyMode.Support => "任務支援器",
        DutyMode.Trust => "親信戰友",
        DutyMode.Squadron => "冒險者分隊",
        DutyMode.Regular => "一般副本",
        DutyMode.Trial => "討伐／殲滅戰",
        DutyMode.Raid => "大型任務",
        DutyMode.Variant => "多變迷宮",
        _ => mode.ToCustomString(),
    };

    public static void Draw()
    {
        MainWindow.SetCurrentTabName("排程器");
        var configuration = Plugin.Configuration;
        var running = Plugin.PlannerRunning;
        var busy = Plugin.States.HasFlag(PluginState.Looping) || Plugin.States.HasFlag(PluginState.Navigating);

        if (running && Plugin.CurrentPlannerItem is { } activeItem)
        {
            var name = ContentHelper.DictionaryContent.TryGetValue(activeItem.TerritoryType, out var activeContent)
                ? activeContent.Name
                : activeItem.TerritoryType.ToString();
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                $"執行中：{configuration.PlannerCurrentIndex + 1}/{configuration.PlannerItems.Count}　{name}　{activeItem.CompletedRuns}/{activeItem.TargetRuns}");
        }
        else if (busy)
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "一般 AutoDuty 正在執行；停止後才能啟動排程。");
        }
        else
        {
            ImGui.TextDisabled("依清單順序執行副本，只有成功完成副本才會增加進度。");
        }

        using (ImRaii.Disabled(busy))
        {
            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("副本模式##PlannerDutyMode", DutyModeName(configuration.PlannerDutyMode)))
            {
                foreach (var mode in SupportedModes)
                {
                    if (ImGui.Selectable(DutyModeName(mode), configuration.PlannerDutyMode == mode))
                    {
                        configuration.PlannerDutyMode = mode;
                        configuration.Save();
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (ImGui.Checkbox("啟用排程器", ref configuration.PlannerEnabled))
                configuration.Save();
            ImGui.SameLine();
            if (ImGui.Checkbox("循環整份排程", ref configuration.PlannerRepeat))
                configuration.Save();
        }

        if (!running)
        {
            using (ImRaii.Disabled(busy))
            {
                if (ImGui.Button("執行排程"))
                {
                    if (!Plugin.TryStartPlanner(out var error))
                        MainWindow.ShowPopup("排程器", error);
                }
            }
        }
        else
        {
            MainWindow.StopResumePause();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(busy || configuration.PlannerItems.Count == 0))
        {
            if (ImGui.Button("重設進度"))
                Plugin.ResetPlannerProgress();
        }
        ImGuiComponents.HelpMarker("手動停止會保留已完成次數；再次執行時會從尚未完成的項目繼續。循環模式完成最後一項後會將全部進度歸零並重來。");

        ImGui.Separator();
        using (ImRaii.Disabled(busy))
        {
            DrawEditor(configuration);
        }
    }

    private static void DrawEditor(Configuration configuration)
    {
        _newItemRuns = Math.Max(1, _newItemRuns);
        ImGui.SetNextItemWidth(110f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("次數##PlannerNewRuns", ref _newItemRuns))
            _newItemRuns = Math.Max(1, _newItemRuns);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##PlannerSearch", "搜尋要加入的副本……", ref _searchText, 100);

        if (configuration.PlannerDutyMode == DutyMode.None)
        {
            ImGui.TextDisabled("請先選擇副本模式。 ");
        }
        else if (ImGui.BeginListBox("##PlannerDutyList", new System.Numerics.Vector2(-1, 145f * ImGuiHelpers.GlobalScale)))
        {
            var level = PlayerHelper.GetCurrentLevelFromSheet();
            foreach (var content in ContentHelper.DictionaryContent.Values
                         .Where(content => content.DutyModes.HasFlag(configuration.PlannerDutyMode))
                         .OrderBy(content => content.ClassJobLevelRequired)
                         .ThenBy(content => content.Name))
            {
                if (!string.IsNullOrWhiteSpace(_searchText) && !(content.Name?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false))
                    continue;

                var canRun = content.CanRun(level, configuration.PlannerDutyMode);
                if (configuration.HideUnavailableDuties && !canRun)
                    continue;
                using (ImRaii.Disabled(!canRun))
                {
                    if (ImGui.Selectable($"L{content.ClassJobLevelRequired} ({content.TerritoryType}) {content.Name}##PlannerAdd{content.TerritoryType}"))
                    {
                        configuration.PlannerItems.Add(new PlannerItem
                        {
                            TerritoryType = content.TerritoryType,
                            TargetRuns = _newItemRuns,
                        });
                        configuration.Save();
                    }
                }
            }
            ImGui.EndListBox();
        }

        if (configuration.PlannerItems.Count == 0)
        {
            ImGui.TextDisabled("排程清單為空。 ");
            return;
        }

        if (!ImGui.BeginTable("##PlannerTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("副本");
        ImGui.TableSetupColumn("路徑", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("次數", ImGuiTableColumnFlags.WidthFixed, 70f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("進度", ImGuiTableColumnFlags.WidthFixed, 65f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 140f * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        for (var i = 0; i < configuration.PlannerItems.Count; i++)
        {
            var item = configuration.PlannerItems[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((i + 1).ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ContentHelper.DictionaryContent.TryGetValue(item.TerritoryType, out var content)
                ? $"L{content.ClassJobLevelRequired} ({item.TerritoryType}) {content.Name}"
                : $"({item.TerritoryType}) <未知副本>");
            if (i == configuration.PlannerCurrentIndex)
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudYellow, "← 目前");
            }

            ImGui.TableNextColumn();
            DrawPathSelector(item, i);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var targetRuns = item.TargetRuns;
            if (ImGui.InputInt($"##PlannerRuns{i}", ref targetRuns, 0, 0))
            {
                item.TargetRuns = Math.Max(1, targetRuns);
                item.CompletedRuns = Math.Clamp(item.CompletedRuns, 0, item.TargetRuns);
                configuration.Save();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{item.CompletedRuns}/{Math.Max(1, item.TargetRuns)}");

            ImGui.TableNextColumn();
            var changed = false;
            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.Button($"上移##PlannerUp{i}"))
                {
                    (configuration.PlannerItems[i - 1], configuration.PlannerItems[i]) = (configuration.PlannerItems[i], configuration.PlannerItems[i - 1]);
                    if (configuration.PlannerCurrentIndex == i) configuration.PlannerCurrentIndex--;
                    else if (configuration.PlannerCurrentIndex == i - 1) configuration.PlannerCurrentIndex++;
                    changed = true;
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(i >= configuration.PlannerItems.Count - 1))
            {
                if (ImGui.Button($"下移##PlannerDown{i}"))
                {
                    (configuration.PlannerItems[i + 1], configuration.PlannerItems[i]) = (configuration.PlannerItems[i], configuration.PlannerItems[i + 1]);
                    if (configuration.PlannerCurrentIndex == i) configuration.PlannerCurrentIndex++;
                    else if (configuration.PlannerCurrentIndex == i + 1) configuration.PlannerCurrentIndex--;
                    changed = true;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button($"刪除##PlannerDelete{i}"))
            {
                configuration.PlannerItems.RemoveAt(i);
                configuration.PlannerCurrentIndex = configuration.PlannerItems.Count == 0
                    ? 0
                    : Math.Clamp(configuration.PlannerCurrentIndex, 0, configuration.PlannerItems.Count - 1);
                changed = true;
                i--;
            }
            if (changed)
                configuration.Save();
        }
        ImGui.EndTable();

        if (configuration.PlannerDutyMode == DutyMode.Trust)
            ImGui.TextDisabled("親信隊友沿用 Main 分頁目前選擇；所有排程項目共用同一組隊友。 ");
    }

    private static void DrawPathSelector(PlannerItem item, int index)
    {
        if (!ContentPathsManager.DictionaryPaths.TryGetValue(item.TerritoryType, out var paths) || paths.Paths.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "無路徑");
            return;
        }

        var selected = string.IsNullOrWhiteSpace(item.PathFileName)
            ? "（自動）"
            : paths.Paths.FirstOrDefault(path => path.FileName.Equals(item.PathFileName, StringComparison.OrdinalIgnoreCase))?.Name ?? "（路徑缺失）";
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##PlannerPath{index}", selected))
            return;

        if (ImGui.Selectable("（自動）", string.IsNullOrWhiteSpace(item.PathFileName)))
        {
            item.PathFileName = null;
            Plugin.Configuration.Save();
        }
        foreach (var path in paths.Paths)
        {
            var isSelected = path.FileName.Equals(item.PathFileName, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(path.Name, isSelected))
            {
                item.PathFileName = path.FileName;
                Plugin.Configuration.Save();
            }
        }
        ImGui.EndCombo();
    }
}
