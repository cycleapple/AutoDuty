using AutoDuty.Helpers;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using System.Diagnostics;

namespace AutoDuty.Windows
{
    using Dalamud.Interface.Utility.Raii;
    using global::AutoDuty.IPC;
    using static Dalamud.Interface.Utility.Raii.ImRaii;

    internal static class InfoTab
    {
        static string infoUrl = "https://docs.google.com/spreadsheets/d/151RlpqRcCpiD_VbQn6Duf-u-S71EP7d0mx3j1PDNoNA";
        static string gitIssueUrl = "https://github.com/ffxivcode/AutoDuty/issues";
        static string punishDiscordUrl = "https://discord.com/channels/1001823907193552978/1236757595738476725";

        private static Configuration Configuration = Plugin.Configuration;

        public static void Draw()
        {
            if (MainWindow.CurrentTabName != "Info")
                MainWindow.CurrentTabName = "Info";
            ImGui.NewLine();
            ImGuiEx.TextWrapped("若需要協助設定 AutoDuty 及其相依插件，請查看下方的設定指南以取得詳細資訊：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("資訊與設定").X) / 2);
            if (ImGui.Button("資訊與設定"))
                Process.Start("explorer.exe", infoUrl);
            ImGui.NewLine();
            ImGuiEx.TextWrapped("上述指南也會列出各路徑的狀態，包括路徑成熟度、模組成熟度與整體穩定性。你也可以查看成功循環時需要留意的附註與事項。若要提出功能需求、回報問題或參與 AutoDuty 開發，請前往 GitHub 建立問題：");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("GitHub 問題回報").X) / 2);
            if (ImGui.Button("GitHub 問題回報"))
                Process.Start("explorer.exe", gitIssueUrl);
            ImGui.NewLine();
            ImGuiEx.TextCentered("若有其他問題，歡迎加入 Discord！");
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Punish Discord").X) / 2);
            if (ImGui.Button("Punish Discord"))
                Process.Start("explorer.exe", punishDiscordUrl);

            ImGui.NewLine();

            int id = 0;

            void PluginInstallLine(ExternalPlugin plugin, string message)
            {
                bool isReady = plugin == ExternalPlugin.BossMod ?
                                   BossMod_IPCSubscriber.IsEnabled :
                                   IPCSubscriber_Common.IsReady(plugin.GetExternalPluginData().name);

                if(!isReady)
                    if (ImGui.Button($"安裝##InstallExternalPlugin_{plugin}_{id++}"))
                        PluginInstaller.InstallPlugin(plugin);

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(isReady ? EzColor.Green : EzColor.Red, plugin.GetExternalPluginName());

                ImGui.NextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(message);
                ImGui.NextColumn();
            }

            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("必要插件").X) / 2);
            ImGui.Text("必要插件");

            ImGui.Columns(3, "PluginInstallerRequired", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod, "協助處理首領戰機制");
            PluginInstallLine(ExternalPlugin.vnav, "提供導航與移動功能");

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("戰鬥插件").X) / 2);
            ImGui.Text("戰鬥插件");

            ImGui.Indent(65f);
            ImGui.TextColored(EzColor.Cyan, "請依個人偏好選擇，並可在設定中進一步調整。");
            ImGui.Unindent(65f);

            ImGui.Columns(3, "PluginInstallerCombat", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.BossMod,              "內建技能循環功能");
            PluginInstallLine(ExternalPlugin.WrathCombo,           "Puni.sh 的專用技能循環插件");
            PluginInstallLine(ExternalPlugin.RotationSolverReborn, "Combat Reborn 的技能循環插件");

            ImGui.Columns(1);
            ImGui.NewLine();
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("建議插件").X) / 2);
            ImGui.Text("建議插件");
            ImGui.NewLine();
            ImGui.Columns(3, "PluginInstallerRecommended", false);
            ImGui.SetColumnWidth(0, 60);
            ImGui.SetColumnWidth(1, 100);

            PluginInstallLine(ExternalPlugin.AntiAFK,      "避免被標記為暫離");
            PluginInstallLine(ExternalPlugin.AutoRetainer, "可供 AutoDuty 呼叫，支援籌備品繳交與丟棄物品");
            PluginInstallLine(ExternalPlugin.Avarice,      "提供身位判定資訊");
            PluginInstallLine(ExternalPlugin.Lifestream,   "提供完整的傳送功能");
            PluginInstallLine(ExternalPlugin.Pandora,      "拾取寶箱與自動開啟防護職姿態");
            PluginInstallLine(ExternalPlugin.Gearsetter,   "推薦可裝備的物品");
            PluginInstallLine(ExternalPlugin.Stylist,      "推薦可裝備的物品");


            ImGui.Columns(1);
        }
    }
}
