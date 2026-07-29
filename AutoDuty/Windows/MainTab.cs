using AutoDuty.Helpers;
using AutoDuty.IPC;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoDuty.Windows
{
    using Dalamud.Interface;
    using Dalamud.Interface.Utility;
    using Data;
    using SharpDX;
    using static Data.Classes;
    using Vector2 = System.Numerics.Vector2;
    using Vector4 = System.Numerics.Vector4;

    internal static class MainTab
    {
        internal static ContentPathsManager.ContentPathContainer? DutySelected;
        internal static readonly (string Normal, string GameFont) Digits = ("0123456789", "");

        private static int _currentStepIndex = -1;
        private static readonly string _pathsURL = "https://github.com/ffxivcode/AutoDuty/tree/master/AutoDuty/Paths";

        // New search text field for filtering duties
        private static string _searchText = string.Empty;

        internal static void Draw()
        {
            MainWindow.CurrentTabName = "Main";

            var dutyMode = Plugin.Configuration.DutyModeEnum;
            var levelingMode = Plugin.LevelingModeEnum;

            static void DrawSearchBar()
            {
                // Set the maximum search to 10 characters
                int inputMaxLength = 10;

                // Calculate the X width of the maximum amount of search characters
                Vector2 _characterWidth = ImGui.CalcTextSize("W");
                float inputMaxWidth = ImGui.CalcTextSize("W").X * inputMaxLength;

                // Set the width of the search box to the calculated width
                ImGui.SetNextItemWidth(inputMaxWidth);

                ImGui.InputTextWithHint("##search", "搜尋副本…", ref _searchText, inputMaxLength);

                // Apply filtering based on the search text
                if (_searchText.Length > 0)
                {
                    // Trim and convert to lowercase for case-insensitive search
                    _searchText = _searchText.Trim().ToLower();
                }
            }

            static void DrawPathSelection()
            {
                if (Plugin.CurrentTerritoryContent == null || !PlayerHelper.IsReady)
                    return;

                using var d = ImRaii.Disabled(Plugin is { InDungeon: true, Stage: > 0 });

                if (ContentPathsManager.DictionaryPaths.TryGetValue(Plugin.CurrentTerritoryContent.TerritoryType, out var container))
                {
                    List<ContentPathsManager.DutyPath> curPaths = container.Paths;
                    if (curPaths.Count > 1)
                    {
                        int                              curPath       = Math.Clamp(Plugin.CurrentPath, 0, curPaths.Count - 1);

                        Dictionary<string, JobWithRole>? pathSelection    = null;
                        JobWithRole                      curJob = Svc.ClientState.LocalPlayer.GetJob().JobToJobWithRole();
                        using (ImRaii.Disabled(curPath <= 0 ||
                                               !Plugin.Configuration.PathSelectionsByPath.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ||
                                               !(pathSelection = Plugin.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]).Any(kvp => kvp.Value.HasJob(Svc.ClientState.LocalPlayer.GetJob()))))
                        {
                            if (ImGui.Button("清除已儲存路徑"))
                            {
                                foreach (KeyValuePair<string, JobWithRole> keyValuePair in pathSelection)
                                    pathSelection[keyValuePair.Key] &= ~curJob;

                                PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);
                                Plugin.Configuration.Save();
                                if (!Plugin.InDungeon)
                                    container.SelectPath(out Plugin.CurrentPath);
                            }
                        }
                        ImGui.SameLine();
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.BeginCombo("##SelectedPath", curPaths[curPath].Name))
                        {
                            foreach ((ContentPathsManager.DutyPath Value, int Index) path in curPaths.Select((value, index) => (Value: value, Index: index)))
                            {
                                if (ImGui.Selectable(path.Value.Name))
                                {
                                    curPath = path.Index;
                                    PathSelectionHelper.AddPathSelectionEntry(Plugin.CurrentTerritoryContent!.TerritoryType);
                                    Dictionary<string, JobWithRole> pathJobs = Plugin.Configuration.PathSelectionsByPath[Plugin.CurrentTerritoryContent.TerritoryType]!;
                                    pathJobs.TryAdd(path.Value.FileName, JobWithRole.None);

                                    foreach (string jobsKey in pathJobs.Keys)
                                        pathJobs[jobsKey] &= ~curJob;

                                    pathJobs[path.Value.FileName] |= curJob;

                                    PathSelectionHelper.RebuildDefaultPaths(Plugin.CurrentTerritoryContent.TerritoryType);

                                    Plugin.Configuration.Save();
                                    Plugin.CurrentPath = curPath;
                                    Plugin.LoadPath();
                                }
                                if (ImGui.IsItemHovered() && !path.Value.PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                                    ImGui.SetTooltip(string.Join("\n", path.Value.PathFile.Meta.Notes));
                            }
                            ImGui.EndCombo();
                        }
                        ImGui.PopItemWidth();

                        if (ImGui.IsItemHovered() && !curPaths[curPath].PathFile.Meta.Notes.All(x => x.IsNullOrEmpty()))
                            ImGui.SetTooltip(string.Join("\n", curPaths[curPath].PathFile.Meta.Notes));

                    }
                }
            }

            if (Plugin.InDungeon)
            {
                if (Plugin.CurrentTerritoryContent == null)
                    Plugin.LoadPath();
                else
                {
                    ImGui.AlignTextToFramePadding();
                    var progress = VNavmesh_IPCSubscriber.IsEnabled ? VNavmesh_IPCSubscriber.Nav_BuildProgress() : 0;
                    if (progress >= 0)
                    {
                        ImGui.Text($"{Plugin.CurrentTerritoryContent.Name} Mesh: Loading: ");
                        ImGui.SameLine();
                        ImGui.ProgressBar(progress, new Vector2(200, 0));
                    }
                    else
                        ImGui.Text($"{Plugin.CurrentTerritoryContent.Name} 導航網格：路徑狀態：{(ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType) ? "已載入" : "無")}");

                    ImGui.Separator();
                    ImGui.Spacing();

                    if (dutyMode == DutyMode.Trust && Plugin.CurrentTerritoryContent != null)
                    {
                        ImGui.Columns(3);
                        using (ImRaii.Disabled())
                            DrawTrustMembers(Plugin.CurrentTerritoryContent);
                        ImGui.Columns(1);
                        ImGui.Spacing();
                    }

                    DrawPathSelection();
                    if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                        MainWindow.GotoAndActions();
                    using (ImRaii.Disabled(!VNavmesh_IPCSubscriber.IsEnabled || !Plugin.InDungeon || !VNavmesh_IPCSubscriber.Nav_IsReady() || !BossMod_IPCSubscriber.IsEnabled))
                    {
                        using (ImRaii.Disabled(!Plugin.InDungeon || !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType)))
                        {
                            if (Plugin.Stage == 0)
                            {
                                if (ImGui.Button("開始"))
                                {
                                    Plugin.LoadPath();
                                    _currentStepIndex = -1;
                                    if (Plugin.MainListClicked)
                                        Plugin.Run(Svc.ClientState.TerritoryType, 0, !Plugin.MainListClicked);
                                    else
                                        Plugin.Run(Svc.ClientState.TerritoryType);
                                }
                            }
                            else
                                MainWindow.StopResumePause();
                            ImGui.SameLine(0, 15);
                        }
                        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                        MainWindow.LoopsConfig();
                        ImGui.PopItemWidth();

                        if (!ImGui.BeginListBox("##MainList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                        if ((VNavmesh_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeMovementPlugin) &&
                            (BossMod_IPCSubscriber.IsEnabled  || Plugin.Configuration.UsingAlternativeBossPlugin)     &&
                            (RSR_IPCSubscriber.IsEnabled      || BossMod_IPCSubscriber.IsEnabled || Plugin.Configuration.UsingAlternativeRotationPlugin))
                        {
                            foreach (var item in Plugin.Actions.Select((Value, Index) => (Value, Index)))
                            {
                                item.Value.DrawCustomText(item.Index, () => ItemClicked(item));
                                //var text = item.Value.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase) ? item.Value.Note : $"{item.Value.ToCustomString()}";
                                ////////////////////////////////////////////////////////////////
                            }

                            if (_currentStepIndex != Plugin.Indexer && _currentStepIndex > -1 && Plugin.Stage > 0)
                            {
                                var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                                _currentStepIndex = Plugin.Indexer;
                                if (_currentStepIndex > 1)
                                    ImGui.SetScrollY((_currentStepIndex - 1) * lineHeight);
                            }
                            else if (_currentStepIndex == -1 && Plugin.Stage > 0)
                            {
                                _currentStepIndex = 0;
                                ImGui.SetScrollY(_currentStepIndex);
                            }

                            if (Plugin.InDungeon && Plugin.Actions.Count < 1 && !ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent.TerritoryType))
                                ImGui.TextColored(new Vector4(0, 255, 0, 1),
                                                  $"找不到以下區域的路徑檔案：\n{TerritoryName.GetTerritoryName(Plugin.CurrentTerritoryContent.TerritoryType).Split('|')[1].Trim()}\n({Plugin.CurrentTerritoryContent.TerritoryType}.json)\n路徑資料夾：\n{Plugin.PathsDirectory.FullName.Replace('\\', '/')}\n請由下列位置下載：\n{_pathsURL}\n或在「建立」頁籤中建立路徑。");
                        }
                        else
                        {
                            if (!VNavmesh_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeMovementPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安裝並載入 vnavmesh 才能導航與移動。\n請加入第三方插件庫：\nhttps://puni.sh/api/repository/veyn");
                            if (!BossMod_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeBossPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安裝並載入 BossMod 才能處理副本機制。\n請加入第三方插件庫：\nhttps://puni.sh/api/repository/veyn");
                            if (!Wrath_IPCSubscriber.IsEnabled && !RSR_IPCSubscriber.IsEnabled && !BossMod_IPCSubscriber.IsEnabled && !Plugin.Configuration.UsingAlternativeRotationPlugin)
                                ImGui.TextColored(new Vector4(255, 0, 0, 1), "AutoDuty 需要安裝並載入循環插件（Wrath Combo、Rotation Solver Reborn 或 BossMod AutoRotation）。");
                        }
                        ImGui.EndListBox();
                    }
                }
            }
            else
            {
                if (!Plugin.States.HasFlag(PluginState.Looping) && !Plugin.Overlay.IsOpen)
                    MainWindow.GotoAndActions();


                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping)))
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored(ImGuiHelper.StateGoodColor, "選擇模式：");
                    ImGui.SameLine(0);
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.BeginCombo("##AutoDutyModeEnum", Plugin.Configuration.AutoDutyModeEnum.ToCustomString()))
                    {
                        foreach (AutoDutyMode mode in Enum.GetValues(typeof(AutoDutyMode)))
                        {
                            if (ImGui.Selectable(mode.ToCustomString(), Plugin.Configuration.AutoDutyModeEnum == mode))
                            {
                                Plugin.Configuration.AutoDutyModeEnum = mode;
                                Plugin.Configuration.Save();
                            }
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.PopItemWidth();
                }

                using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                {
                    if (!Plugin.States.HasFlag(PluginState.Looping))
                    {
                        if (ImGui.Button("執行"))
                        {
                            if (Plugin.Configuration.DutyModeEnum == DutyMode.None)
                                MainWindow.ShowPopup("錯誤", "請先選擇要執行的副本模式。");
                            else if (Svc.Party.PartyId > 0 && (Plugin.Configuration.DutyModeEnum == DutyMode.Support || Plugin.Configuration.DutyModeEnum == DutyMode.Squadron || Plugin.Configuration.DutyModeEnum == DutyMode.Trust))
                                MainWindow.ShowPopup("錯誤", "使用任務支援器、冒險者分隊或親信戰友時不可處於玩家隊伍中。");
                            else if (Plugin.Configuration.DutyModeEnum == DutyMode.Regular && !Plugin.Configuration.Unsynced && !Plugin.Configuration.OverridePartyValidation && Svc.Party.PartyId == 0)
                                MainWindow.ShowPopup("錯誤", "執行一般副本時必須組成四人隊伍。");
                            else if (Plugin.Configuration.DutyModeEnum == DutyMode.Regular && !Plugin.Configuration.Unsynced && !Plugin.Configuration.OverridePartyValidation && !ObjectHelper.PartyValidation())
                                MainWindow.ShowPopup("錯誤", "隊伍職責組成不符合一般副本的進入條件。");
                            else if (ContentPathsManager.DictionaryPaths.ContainsKey(Plugin.CurrentTerritoryContent?.TerritoryType ?? 0))
                                Plugin.Run();
                            else
                                MainWindow.ShowPopup("錯誤", $"找不到 {Plugin.CurrentTerritoryContent?.TerritoryType} {Plugin.CurrentTerritoryContent?.Name} 的路徑。");
                        }
                    }
                    else
                        MainWindow.StopResumePause();
                }



                using (ImRaii.Disabled(Plugin.States.HasFlag(PluginState.Looping)))
                {
                    switch (Plugin.Configuration.AutoDutyModeEnum)
                    {
                        case AutoDutyMode.Looping:
                        {
                            using (ImRaii.Disabled(Plugin.CurrentTerritoryContent == null))
                            {
                                ImGui.SameLine(0, 15);
                                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                MainWindow.LoopsConfig();
                                ImGui.PopItemWidth();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.TextColored(Plugin.Configuration.DutyModeEnum == DutyMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, "選擇副本模式：");
                            ImGui.SameLine(0);
                            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginCombo("##DutyModeEnum", Plugin.Configuration.DutyModeEnum.ToCustomString()))
                            {
                                foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                {
                                    if (ImGui.Selectable(mode.ToCustomString(), Plugin.Configuration.DutyModeEnum == mode))
                                    {
                                        Plugin.Configuration.DutyModeEnum = mode;
                                        Plugin.Configuration.Save();
                                    }
                                }
                                ImGui.EndCombo();
                            }
                            ImGui.PopItemWidth();

                            if (Plugin.Configuration.DutyModeEnum != DutyMode.None)
                            {
                                if (Plugin.Configuration.DutyModeEnum == DutyMode.Support || Plugin.Configuration.DutyModeEnum == DutyMode.Trust)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.TextColored(Plugin.LevelingModeEnum == LevelingMode.None ? ImGuiHelper.StateBadColor : ImGuiHelper.StateGoodColor, "選擇練等模式：");
                                    ImGui.SameLine(0);

                                    ImGuiComponents.HelpMarker("練等模式會依角色等級與平均物品品級，選擇運作最穩定的副本。\n" +
                                                               (Plugin.Configuration.DutyModeEnum != DutyMode.Trust ?
                                                                    string.Empty :
                                                                    "隊伍模式會平均提升親信戰友等級。\n單人模式只會將親信戰友提升到所需等級。") +
                                                               "\n\n此功能不一定會選擇最高等級副本，而會依照穩定副本清單選擇。");
                                    ImGui.SameLine(0);
                                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                    if (ImGui.BeginCombo("##LevelingModeEnum", Plugin.LevelingModeEnum switch
                                        {
                                            LevelingMode.None => "無",
                                            _ => $"{Plugin.LevelingModeEnum.ToCustomString().Replace(Plugin.Configuration.DutyModeEnum.ToString(), null)} Auto".Trim()
                                        }))
                                    {
                                        if (ImGui.Selectable("無", Plugin.LevelingModeEnum == LevelingMode.None))
                                        {
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                            Plugin.Configuration.Save();
                                        }

                                        LevelingMode autoLevelMode = (Plugin.Configuration.DutyModeEnum == DutyMode.Support ? LevelingMode.Support : LevelingMode.Trust_Group);
                                        if (ImGui.Selectable($"{autoLevelMode.ToCustomString().Replace(Plugin.Configuration.DutyModeEnum.ToString(), null)} Auto".Trim(), Plugin.LevelingModeEnum == autoLevelMode))
                                        {
                                            Plugin.LevelingModeEnum = autoLevelMode;
                                            Plugin.Configuration.Save();
                                            if (Plugin.Configuration.AutoEquipRecommendedGear)
                                                AutoEquipHelper.Invoke();
                                        }

                                        if (Plugin.Configuration.DutyModeEnum == DutyMode.Trust)
                                            if (ImGui.Selectable($"{LevelingMode.Trust_Solo.ToCustomString().Replace(Plugin.Configuration.DutyModeEnum.ToString(), null)} Auto".Trim(), Plugin.LevelingModeEnum == LevelingMode.Trust_Solo))
                                            {
                                                Plugin.LevelingModeEnum = LevelingMode.Trust_Solo;
                                                Plugin.Configuration.Save();
                                                if (Plugin.Configuration.AutoEquipRecommendedGear)
                                                    AutoEquipHelper.Invoke();
                                            }


                                        ImGui.EndCombo();
                                    }

                                    ImGui.PopItemWidth();
                                }

                                if (Plugin.Configuration.DutyModeEnum == DutyMode.Support && levelingMode == LevelingMode.Support)
                                {
                                    if (ImGui.Checkbox("練等時優先使用親信戰友而非任務支援器", ref Plugin.Configuration.PreferTrustOverSupportLeveling))
                                        Plugin.Configuration.Save();
                                }

                                if (Plugin.Configuration.DutyModeEnum == DutyMode.Trust && Player.Available)
                                {
                                    ImGui.Separator();
                                    if (DutySelected != null && DutySelected.Content.TrustMembers.Count > 0)
                                    {
                                        ImGuiEx.LineCentered(() => ImGuiEx.TextUnderlined("選擇親信戰友隊伍"));


                                        TrustHelper.ResetTrustIfInvalid();
                                        for (int i = 0; i < Plugin.Configuration.SelectedTrustMembers.Length; i++)
                                        {
                                            TrustMemberName? member = Plugin.Configuration.SelectedTrustMembers[i];

                                            if (member is null)
                                                continue;

                                            if (DutySelected.Content.TrustMembers.All(x => x.MemberName != member))
                                            {
                                                Svc.Log.Debug($"Killing {member}");
                                                Plugin.Configuration.SelectedTrustMembers[i] = null;
                                            }
                                        }

                                        ImGui.Columns(3);
                                        using (ImRaii.Disabled(Plugin.TrustLevelingEnabled && TrustHelper.Members.Any(tm => tm.Value.Level < tm.Value.LevelCap)))
                                        {
                                            DrawTrustMembers(DutySelected.Content);
                                        }

                                        //ImGui.Columns(3, null, false);
                                        if (DutySelected.Content.TrustMembers.Count == 7)
                                            ImGui.NextColumn();

                                        if (ImGui.Button("重新整理", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                                        {
                                            if (InventoryHelper.CurrentItemLevel < 370)
                                                Plugin.LevelingModeEnum = LevelingMode.None;
                                            TrustHelper.ClearCachedLevels();

                                            SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]),  () => TrustHelper.State == ActionState.None);
                                            SchedulerHelper.ScheduleAction("Refresh Levels - EW",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]),  () => TrustHelper.State == ActionState.None);
                                            SchedulerHelper.ScheduleAction("Refresh Levels - DT",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                                        }

                                        ImGui.NextColumn();
                                        ImGui.Columns(1);
                                    }
                                    else if (ImGui.Button("更新親信戰友等級"))
                                    {
                                        if (InventoryHelper.CurrentItemLevel < 370)
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                        TrustHelper.ClearCachedLevels();

                                        SchedulerHelper.ScheduleAction("Refresh Levels - ShB", () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[837u]),  () => TrustHelper.State == ActionState.None);
                                        SchedulerHelper.ScheduleAction("Refresh Levels - EW",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[952u]),  () => TrustHelper.State == ActionState.None);
                                        SchedulerHelper.ScheduleAction("Refresh Levels - DT",  () => TrustHelper.GetLevels(ContentHelper.DictionaryContent[1167u]), () => TrustHelper.State == ActionState.None);
                                    }
                                }

                                DrawPathSelection();
                                ImGui.Separator();

                                DrawSearchBar();
                                ImGui.SameLine();
                                if (ImGui.Checkbox("隱藏無法進入的副本", ref Plugin.Configuration.HideUnavailableDuties))
                                    Plugin.Configuration.Save();
                                if (Plugin.Configuration.DutyModeEnum is DutyMode.Regular or DutyMode.Trial or DutyMode.Raid)
                                {
                                    if (ImGuiEx.CheckboxWrapped("解除限制", ref Plugin.Configuration.Unsynced))
                                        Plugin.Configuration.Save();
                                }
                            }

                            break;
                        }
                        case AutoDutyMode.Playlist:
                            ImGui.Separator();
                            break;
                        default:
                            Plugin.Configuration.AutoDutyModeEnum = AutoDutyMode.Looping;
                            break;
                    }

                    ushort ilvl = InventoryHelper.CurrentItemLevel;
                    if (!ImGui.BeginListBox("##DutyList", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y))) return;

                    if (Player.Job.GetCombatRole() == CombatRole.NonCombat)
                    {
                        ImGuiEx.TextWrapped(new Vector4(255, 1, 0, 1), "請切換為戰鬥職業後再使用 AutoDuty。");
                    }
                    else if (Player.Job == Job.BLU && Plugin.Configuration.DutyModeEnum is not (DutyMode.Regular or DutyMode.Trial or DutyMode.Raid))
                    {
                        ImGuiEx.TextWrapped(new Vector4(0, 1, 1, 1), "青魔法師無法執行親信戰友、任務支援器、冒險者分隊或特殊迷宮。請切換職業或選擇其他分類。");
                    }
                    else if (VNavmesh_IPCSubscriber.IsEnabled && BossMod_IPCSubscriber.IsEnabled)
                    {
                        if (PlayerHelper.IsReady)
                        {
                            switch (Plugin.Configuration.AutoDutyModeEnum)
                            {
                                case AutoDutyMode.Looping:
                                    if (Plugin.LevelingModeEnum != LevelingMode.None)
                                    {
                                        if (Player.Job.GetCombatRole() == CombatRole.NonCombat ||
                                            (Plugin.LevelingModeEnum.IsTrustLeveling() &&
                                             (ilvl < 370 || Plugin.CurrentPlayerItemLevelandClassJob.Value != null && Plugin.CurrentPlayerItemLevelandClassJob.Value != Player.Job)))
                                        {
                                            Svc.Log.Debug($"You are on a non-compatible job: {Player.Job.GetCombatRole()}, or your doing trust and your iLvl({ilvl}) is below 370, or your iLvl has changed, Disabling Leveling Mode");
                                            Plugin.LevelingModeEnum = LevelingMode.None;
                                        }
                                        else if (ilvl > 0 && ilvl != Plugin.CurrentPlayerItemLevelandClassJob.Key)
                                        {
                                            Svc.Log.Debug($"Your iLvl has changed, Selecting new Duty.");
                                            Plugin.CurrentTerritoryContent = LevelingHelper.SelectHighestLevelingRelevantDuty(Plugin.LevelingModeEnum);
                                        }
                                        else
                                        {
                                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), $"練等模式：等級 {Player.Level}（物品品級 {ilvl}）");
                                            foreach (var item in LevelingHelper.LevelingDuties.Select((Value, Index) => (Value, Index)))
                                            {
                                                if (Plugin.Configuration.DutyModeEnum == DutyMode.Trust && !item.Value.DutyModes.HasFlag(DutyMode.Trust))
                                                    continue;
                                                var disabled = !item.Value.CanRun();
                                                if (!Plugin.Configuration.HideUnavailableDuties || !disabled)
                                                {
                                                    using (ImRaii.Disabled(disabled))
                                                    {
                                                        ImGuiEx.TextWrapped(item.Value == Plugin.CurrentTerritoryContent ? new Vector4(0, 1, 1, 1) : new Vector4(1, 1, 1, 1),
                                                                            $"L{item.Value.ClassJobLevelRequired} (i{item.Value.ItemLevelRequired}): {item.Value.EnglishName}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Dictionary<uint, Content> dictionary = ContentHelper.DictionaryContent.Where(x => x.Value.DutyModes.HasFlag(Plugin.Configuration.DutyModeEnum)).ToDictionary();

                                        if (dictionary.Count > 0 && PlayerHelper.IsReady)
                                        {
                                            short level = PlayerHelper.GetCurrentLevelFromSheet();
                                            foreach ((uint _, Content? content) in dictionary)
                                            {
                                                // Apply search filter
                                                if (!string.IsNullOrWhiteSpace(_searchText) && !content.Name.ToLower().Contains(_searchText))
                                                    continue; // Skip duties that do not match the search text

                                                bool canRun = content.CanRun(level);
                                                using (ImRaii.Disabled(!canRun))
                                                {
                                                    if (Plugin.Configuration.HideUnavailableDuties && !canRun)
                                                        continue;
                                                    if (ImGui.Selectable($"L{content.ClassJobLevelRequired} ({content.TerritoryType}) {content.Name}", DutySelected?.id == content.TerritoryType))
                                                    {
                                                        DutySelected                   = ContentPathsManager.DictionaryPaths[content.TerritoryType];
                                                        Plugin.CurrentTerritoryContent = content;
                                                        DutySelected.SelectPath(out Plugin.CurrentPath);
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (PlayerHelper.IsReady)
                                                ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), "請選擇任務支援器、親信戰友、冒險者分隊或一般副本，以顯示副本清單。");
                                        }
                                    }

                                    break;
                                case AutoDutyMode.Playlist:
                                    for (int i = 0; i < Plugin.PlaylistCurrent.Count; i++)
                                    {
                                        PlaylistEntry entry = Plugin.PlaylistCurrent[i];

                                        ImGui.AlignTextToFramePadding();
                                        ImGui.SetItemAllowOverlap();
                                        if (ImGui.Selectable($"{i}:##Playlist{i+1}Entry", Plugin.PlaylistIndex == i, ImGuiSelectableFlags.AllowItemOverlap))
                                            Plugin.PlaylistIndex = i;
                                        ImGui.SameLine(0, 10);

                                        //ImGui.AlignTextToFramePadding();
                                        //ImGui.Text($"{i}:"); // {entry.dutyMode} {entry.id}");
                                        //ImGui.SameLine(0, 0);

                                        ContentPathsManager.ContentPathContainer entryContainer = ContentPathsManager.DictionaryPaths[entry.Id];
                                        Content                                  entryContent   = ContentHelper.DictionaryContent[entry.Id];



                                        ImGui.PushItemWidth(80f.Scale());
                                        if (ImGui.InputInt($"##Playlist{i}Count", ref entry.count, step: 1, stepFast: 2, @"%dx"))
                                            entry.count = Math.Max(1, entry.count);

                                        ImGui.PopItemWidth();
                                        ImGui.SameLine();

                                        ImGui.PushItemWidth(100f.Scale());
                                        if (ImGui.BeginCombo($"##Playlist{i}DutyModeEnum", entry.DutyMode.ToCustomString()))
                                        {
                                            foreach (DutyMode mode in Enum.GetValues(typeof(DutyMode)))
                                            {
                                                if (mode == DutyMode.None)
                                                    continue;

                                                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiHelper.StateGoodColor, entryContent.DutyModes.HasFlag(mode)))
                                                {
                                                    if (ImGui.Selectable(mode.ToCustomString(), entry.DutyMode == mode))
                                                        entry.DutyMode = mode;
                                                }
                                            }

                                            ImGui.EndCombo();
                                        }

                                        ImGui.PopItemWidth();
                                        ImGui.SameLine();
                                        ImGui.PushItemWidth((entryContainer.Paths.Count > 1 ? (ImGui.GetContentRegionAvail().X - 107f.Scale()) / 2f : ImGui.GetContentRegionAvail().X - 100f.Scale()));
                                        if (ImGui.BeginCombo($"##Playlist{i}DutySelection", $"({entry.Id}) {entryContent.Name}"))
                                        {
                                            short level = PlayerHelper.GetCurrentLevelFromSheet();
                                            DrawSearchBar();

                                            foreach (uint key in ContentPathsManager.DictionaryPaths.Keys)
                                            {
                                                Content content = ContentHelper.DictionaryContent[key];

                                                if (!string.IsNullOrWhiteSpace(_searchText) && !(content.Name?.ToLower().Contains(_searchText) ?? false))
                                                    continue;

                                                if (content.DutyModes.HasFlag(entry.DutyMode) && content.CanRun(level, entry.DutyMode))
                                                    if (ImGui.Selectable($"({key}) {content.Name}", entry.Id == key))
                                                        entry.Id = key;
                                            }

                                            ImGui.EndCombo();
                                        }

                                        if(entry.Id != entryContent.TerritoryType)
                                            continue;

                                        if (entryContainer.Paths.Count > 1)
                                        {
                                            ImGui.SameLine();
                                            if (ImGui.BeginCombo($"##Playlist{i}PathSelection", entryContainer.Paths.First(dp => dp.FileName == entry.path).Name))
                                            {
                                                foreach (ContentPathsManager.DutyPath path in entryContainer.Paths)
                                                    if(ImGui.Selectable(path.Name, path.FileName == entry.path))
                                                        entry.path = path.FileName;

                                                ImGui.EndCombo();
                                            }
                                        }


                                        ImGui.PopItemWidth();
                                        ImGui.SameLine();

                                        using (ImRaii.Disabled(i <= 0))
                                        {
                                            if (ImGuiComponents.IconButton($"Playlist{i}Up", FontAwesomeIcon.ArrowUp))
                                            {
                                                Plugin.PlaylistCurrent.Remove(entry);
                                                Plugin.PlaylistCurrent.Insert(i - 1, entry);
                                            }
                                        }

                                        ImGui.SameLine();

                                        using(ImRaii.Disabled(Plugin.PlaylistCurrent.Count <= i+1))
                                        {
                                            if (ImGuiComponents.IconButton($"Playlist{i}Down", FontAwesomeIcon.ArrowDown))
                                            {
                                                Plugin.PlaylistCurrent.Remove(entry);
                                                Plugin.PlaylistCurrent.Insert(i+1, entry);
                                            }
                                        }

                                        ImGui.SameLine();

                                        if (ImGuiComponents.IconButton($"Playlist{i}Trash", FontAwesomeIcon.TrashAlt))
                                            Plugin.PlaylistCurrent.RemoveAt(i);
                                    }

                                    if (ImGuiComponents.IconButton("PlaylistAdd", FontAwesomeIcon.Plus))
                                        Plugin.PlaylistCurrent.Add(new PlaylistEntry { DutyMode = Plugin.PlaylistCurrent.Any() ? Plugin.PlaylistCurrent.Last().DutyMode : DutyMode.Support });

                                    break;
                            }
                        }
                        else
                            ImGuiEx.TextWrapped(new Vector4(0, 1, 0, 1), "處理中……");
                    }
                    else
                    {
                        if (!VNavmesh_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), "AutoDuty 需要安裝並載入 vnavmesh 才能正確導航與移動。請加入第三方插件庫：\nhttps://puni.sh/api/repository/veyn");
                        if (!BossMod_IPCSubscriber.IsEnabled)
                            ImGuiEx.TextWrapped(new Vector4(255, 0, 0, 1), "AutoDuty 需要安裝並載入 BossMod 才能正確處理副本機制。請加入第三方插件庫：\nhttps://puni.sh/api/repository/veyn");
                    }
                    ImGui.EndListBox();
                }
            }
        }

        private static void DrawTrustMembers(Content content)
        {
            foreach (TrustMember member in content.TrustMembers)
            {
                bool       enabled        = Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName);
                CombatRole playerRole     = Player.Job.GetCombatRole();
                int        numberSelected = Plugin.Configuration.SelectedTrustMembers.Count(x => x != null);

                TrustMember?[] members = Plugin.Configuration.SelectedTrustMembers.Select(tmn => tmn != null ? TrustHelper.Members[(TrustMemberName)tmn] : null).ToArray();

                bool canSelect = members.CanSelectMember(member, playerRole) && member.Level >= content.ClassJobLevelRequired;

                using (ImRaii.Disabled(!enabled && (numberSelected == 3 || !canSelect)))
                {
                    if (ImGui.Checkbox($"###{member.Index}{content.Id}", ref enabled))
                    {
                        if (enabled)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                if (Plugin.Configuration.SelectedTrustMembers[i] is null)
                                {
                                    Plugin.Configuration.SelectedTrustMembers[i] = member.MemberName;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (Plugin.Configuration.SelectedTrustMembers.Where(x => x != null).Any(x => x == member.MemberName))
                            {
                                int idx = Plugin.Configuration.SelectedTrustMembers.IndexOf(x => x != null && x == member.MemberName);
                                Plugin.Configuration.SelectedTrustMembers[idx] = null;
                            }
                        }

                        Plugin.Configuration.Save();
                    }
                }

                ImGui.SameLine(0, 2);
                ImGui.SetItemAllowOverlap();
                ImGui.TextColored(member.Role switch
                {
                    TrustRole.DPS => ImGuiHelper.RoleDPSColor,
                    TrustRole.Healer => ImGuiHelper.RoleHealerColor,
                    TrustRole.Tank => ImGuiHelper.RoleTankColor,
                    TrustRole.AllRounder => ImGuiHelper.RoleAllRounderColor,
                    _ => Vector4.One
                }, member.Name);
                if (member.Level > 0)
                {
                    ImGui.SameLine(0, 2);
                    ImGuiEx.TextV(member.Level < member.LevelCap ? ImGuiHelper.White : ImGuiHelper.MaxLevelColor, $"{member.Level.ToString().ReplaceByChar(Digits.Normal, Digits.GameFont)}");
                }

                ImGui.NextColumn();
            }
        }

        private static void ItemClicked((PathAction, int) item)
        {
            if (item.Item2 == Plugin.Indexer || item.Item1.Name.StartsWith("<--", StringComparison.InvariantCultureIgnoreCase))
            {
                Plugin.Indexer = -1;
                Plugin.MainListClicked = false;
            }
            else
            {
                Plugin.Indexer = item.Item2;
                Plugin.MainListClicked = true;
            }
        }

        internal static void PathsUpdated()
        {
            DutySelected = null;
        }
    }
}
