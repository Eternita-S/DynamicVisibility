using Dalamud.Configuration;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Internal;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Logging;
using Dalamud.Plugin;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DynamicVisibility
{
    class DynamicVisibility : IDalamudPlugin
    {
        Dictionary<ushort, TerritoryType> zones;
        bool CfgOpen = false;
        Configuration Cfg;
        bool OnlySelected = false;
        string Filter = "";
        bool JumpToCurrent = false;
        bool IsInCutscene = false;
        bool VisWasDisabledInCS = false;

        public string Name => "DynamicVisibility";

        public void Dispose()
        {
            Svc.PluginInterface.UiBuilder.Draw -= Draw;
            Svc.ClientState.TerritoryChanged -= PerformCheck;
            Svc.ClientState.Login -= PerformCheck;
            Svc.Framework.Update -= Tick;
            Svc.Commands.RemoveHandler("/dvis");
        }

        public DynamicVisibility(DalamudPluginInterface pluginInterface)
        {
            pluginInterface.Create<Svc>();
            zones = Svc.Data.GetExcelSheet<TerritoryType>().ToDictionary(row => (ushort)row.RowId, row => row);
            Cfg = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Svc.PluginInterface.UiBuilder.Draw += Draw;
            Svc.PluginInterface.UiBuilder.OpenConfigUi += delegate { CfgOpen = true; };
            Svc.ClientState.TerritoryChanged += PerformCheck;
            Svc.ClientState.Login += PerformCheck;
            Svc.Framework.Update += Tick;
            Svc.Commands.AddHandler("/dvis", new CommandInfo(delegate
            {
                CfgOpen = !CfgOpen;
            }));
            PerformCheck();
        }

        private void Tick(Framework framework)
        {
            if (Cfg.EnableCutsceneFix)
            {
                if (Svc.ClientState == null) return;
                var c = Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
                    || Svc.Condition[ConditionFlag.WatchingCutscene78];
                if (IsInCutscene != c)
                {
                    if (c)
                    {
                        //cutscene begins
                        if (GetVisibilityState())
                        {
                            SetVisibilityEnabled(false);
                            VisWasDisabledInCS = true;
                            Svc.PluginInterface.UiBuilder.AddNotification("Visibility plugin temporarily disabled because cutscene has begun", "Dynamic Visibiliy", NotificationType.Error);
                        }
                    }
                    else
                    {
                        //cutscene ends
                        if (VisWasDisabledInCS)
                        {
                            SetVisibilityEnabled(true);
                            VisWasDisabledInCS = false;
                            Svc.PluginInterface.UiBuilder.AddNotification("Visibility plugin reenabled", "Dynamic Visibiliy", NotificationType.Success);
                        }
                    }
                }
                IsInCutscene = c;
            }
        }

        void Draw()
        {
            if (!CfgOpen) return;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(550, 250));
            if (ImGui.Begin("Dynamic Visibility configuration", ref CfgOpen))
            {
                ImGui.Checkbox("Enable territory black/whitelist", ref Cfg.EnableTerritoryList);
                ImGui.Checkbox("Temporarily disable Visibility in cutscenes", ref Cfg.EnableCutsceneFix);

                if (Cfg.EnableTerritoryList)
                {
                    if (ImGui.Button("Mode: " + (Cfg.IsBlacklist ? "Blacklist" : "Whitelist")))
                    {
                        Cfg.IsBlacklist = !Cfg.IsBlacklist;
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150f);
                    ImGui.InputTextWithHint("##DVisFilter", "Filter...", ref Filter, 100);
                    ImGui.SameLine();
                    ImGui.Checkbox("Display only selected", ref OnlySelected);
                    ImGui.SameLine();
                    if (ImGui.Button("To current zone")) JumpToCurrent = true;
                    ImGui.Text("Visibility will be " + (Cfg.IsBlacklist ? "disabled" : "enabled") + " in these zones:");
                    ImGui.BeginChild("##DVisZList");
                    foreach (var e in zones)
                    {
                        if (e.Value.PlaceName.Value.Name == "") continue;
                        if (OnlySelected && !Cfg.TerrList.Contains(e.Key)) continue;
                        if (Filter.Length > 0 && !e.Key.ToString().Contains(Filter)
                            && !e.Value.PlaceName.Value.Name.ToString().ToLower().Contains(Filter.ToLower())) continue;
                        var colored = false;
                        if (e.Key == Svc.ClientState?.TerritoryType)
                        {
                            if (JumpToCurrent) ImGui.SetScrollHereY();
                            ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ff00);
                            colored = true;
                        }
                        var chk = Cfg.TerrList.Contains(e.Key);
                        ImGui.Checkbox(e.Key + " | " + e.Value.PlaceName.Value.Name + "##" + e.Key, ref chk);
                        if (colored) ImGui.PopStyleColor();
                        if (chk)
                        {
                            if (!Cfg.TerrList.Contains(e.Key)) Cfg.TerrList.Add(e.Key);
                        }
                        else
                        {
                            if (Cfg.TerrList.Contains(e.Key)) Cfg.TerrList.Remove(e.Key);
                        }
                    }
                }
                ImGui.EndChild();
                ImGui.End();
                ImGui.PopStyleVar();
            }
            JumpToCurrent = false;
            if (!CfgOpen)
            {
                Svc.PluginInterface.SavePluginConfig(Cfg);
                Svc.PluginInterface.UiBuilder.AddNotification("Configuration saved", "Dynamic Visibility", NotificationType.Success);
                PerformCheck();
            }
        }
        
        void PerformCheck(object _, object __)
        {
            PerformCheck();
        }

        void PerformCheck()
        {
            if (Svc.ClientState == null) return;
            PerformCheck(null, Svc.ClientState.TerritoryType);
        }

        void PerformCheck(object _, ushort territory)
        {
            if (!Cfg.EnableTerritoryList) return;
            try
            {
                if (Cfg.TerrList.Contains(territory))
                {
                    SetVisibilityEnabled(!Cfg.IsBlacklist);
                }
                else
                {
                    SetVisibilityEnabled(Cfg.IsBlacklist);
                }
            }
            catch (Exception e)
            {
                Svc.Chat.Print("Dynamic Visibility has ran into an error: " + e.Message);
                Svc.Chat.Print(e.StackTrace);
            }
        }

        void SetVisibilityEnabled(bool enabled)
        {
            try
            {
                var vis = GetVisibilityPlugin();
                if(vis == null)
                {
                    Svc.Chat.Print("Could not find visibility plugin");
                    return;
                }
                var vcfg = vis.GetType().GetField("Configuration");
                var chmth = vcfg.GetValue(vis).GetType().GetMethod("ChangeSetting", BindingFlags.NonPublic | BindingFlags.Instance,
                  null, new Type[] { typeof(string), typeof(int) }, null);
                chmth.Invoke(vcfg.GetValue(vis), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "Enabled", enabled?1:0 }, null);
                //pi.Framework.Gui.Chat.Print("Set visibility state to: " + enabled);
            }
            catch (Exception e)
            {
                Svc.Chat.Print("Failed to set visibility state: " + e.Message);
                Svc.Chat.Print(e.StackTrace);
            }
        }

        bool GetVisibilityState()
        {
            try
            {
                var vis = GetVisibilityPlugin();
                if (vis == null)
                {
                    Svc.Chat.Print("Could not find visibility plugin");
                    return false;
                }
                var vcfg = vis.GetType().GetField("PluginConfiguration");
                var enabled = vcfg.GetValue(vis).GetType().GetProperty("Enabled");
                return (bool)enabled.GetValue(vcfg.GetValue(vis));
                //Svc.Chat.Print("Set visibility state to: " + enabled);
            }
            catch (Exception e)
            {
                Svc.Chat.Print("Failed to read visibility plugin state: " + e.Message);
                Svc.Chat.Print(e.StackTrace);
                return false;
            }
        }

        IDalamudPlugin GetVisibilityPlugin()
        {
            try
            {
                var assembly = Svc.PluginInterface.GetType().Assembly;
                var pluginManager = assembly.
                    GetType("Dalamud.Service`1", true).MakeGenericType(assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)).
                    GetMethod("Get").Invoke(null, BindingFlags.Default, null, new object[] { }, null);
                var installedPlugins = (System.Collections.IList)pluginManager.GetType().GetProperty("InstalledPlugins").GetValue(pluginManager);

                foreach (var t in installedPlugins)
                {
                    if ((string)t.GetType().GetProperty("Name").GetValue(t) == "Visibility")
                    {
                        return (IDalamudPlugin)t.GetType().GetField("instance", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(t);
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                Svc.Chat.Print("Can't find visibility plugin: " + e.Message);
                Svc.Chat.Print(e.StackTrace);
                return null;
            }
        }
    }

    [Serializable]
    class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        public HashSet<ushort> TerrList = new HashSet<ushort>();
        public bool IsBlacklist = false;
        public bool EnableTerritoryList = true;
        public bool EnableCutsceneFix = true;
    }
}
