using Dalamud.Configuration;
using Dalamud.Game.Internal.Gui.Toast;
using Dalamud.Plugin;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DynamicVisibility
{
    class DynamicVisibility : IDalamudPlugin
    {
        internal DalamudPluginInterface pi;
        Dictionary<ushort, TerritoryType> zones;
        bool CfgOpen = false;
        Configuration Cfg;
        bool OnlySelected = false;
        string Filter = "";
        bool JumpToCurrent = false;

        public string Name => "DynamicVisibility";

        public void Dispose()
        {
            pi.UiBuilder.OnBuildUi -= Draw;
            pi.ClientState.TerritoryChanged -= PerformCheck;
            pi.ClientState.OnLogin -= PerformCheck;
            pi.Dispose();
        }

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            pi = pluginInterface;
            zones = pluginInterface.Data.GetExcelSheet<TerritoryType>().ToDictionary(row => (ushort)row.RowId, row => row);
            Cfg = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pi.UiBuilder.OnBuildUi += Draw;
            pi.UiBuilder.OnOpenConfigUi += delegate { CfgOpen = true; };
            pi.ClientState.TerritoryChanged += PerformCheck;
            pi.ClientState.OnLogin += PerformCheck;
            PerformCheck();
        }

        void Draw()
        {
            if (!CfgOpen) return;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(550, 250));
            if(ImGui.Begin("Dynamic Visibility configuration", ref CfgOpen))
            {
                if(ImGui.Button("Mode: " + (Cfg.IsBlacklist ? "Blacklist" : "Whitelist")))
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
                ImGui.Text("Visibility will be "+(Cfg.IsBlacklist?"disabled":"enabled")+" in these zones:");
                ImGui.BeginChild("##DVisZList");
                foreach(var e in zones)
                {
                    if (e.Value.PlaceName.Value.Name == "") continue;
                    if (OnlySelected && !Cfg.TerrList.Contains(e.Key)) continue;
                    if (Filter.Length > 0 && !e.Key.ToString().Contains(Filter)
                        && !e.Value.PlaceName.Value.Name.ToString().ToLower().Contains(Filter.ToLower())) continue;
                    var colored = false;
                    if (e.Key == pi.ClientState?.TerritoryType)
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
            JumpToCurrent = false;
            if (!CfgOpen)
            {
                pi.SavePluginConfig(Cfg);
                pi.Framework.Gui.Toast.ShowQuest("Configuration saved", new QuestToastOptions()
                {
                    DisplayCheckmark = true, PlaySound = true
                });
                PerformCheck();
            }
        }
        
        void PerformCheck(object _, object __)
        {
            PerformCheck();
        }

        void PerformCheck()
        {
            if (pi.ClientState == null) return;
            PerformCheck(null, pi.ClientState.TerritoryType);
        }

        void PerformCheck(object _, ushort territory)
        {
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
                pi.Framework.Gui.Chat.Print("Dynamic Visibility has ran into an error: " + e.Message);
                pi.Framework.Gui.Chat.Print(e.StackTrace);
            }
        }

        void SetVisibilityEnabled(bool enabled)
        {
            try
            {
                var vis = GetVisibilityPlugin();
                if(vis == null)
                {
                    pi.Framework.Gui.Chat.Print("Could not find visibility plugin");
                    return;
                }
                var vcfg = vis.GetType().GetField("PluginConfiguration");
                var chmth = vcfg.GetValue(vis).GetType().GetMethod("ChangeSetting", BindingFlags.NonPublic | BindingFlags.Instance,
                  null, new Type[] { typeof(string), typeof(int) }, null);
                chmth.Invoke(vcfg.GetValue(vis), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "Enabled", enabled?1:0 }, null);
                //pi.Framework.Gui.Chat.Print("Set visibility state to: " + enabled);
            }
            catch (Exception e)
            {
                pi.Framework.Gui.Chat.Print("Failed to set visibility state: " + e.Message);
                pi.Framework.Gui.Chat.Print(e.StackTrace);
            }
        }

        IDalamudPlugin GetVisibilityPlugin()
        {
            try
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var d = (Dalamud.Dalamud)pi.GetType().GetField("dalamud", flags).GetValue(pi);
                var pmanager = d.GetType().GetProperty("PluginManager", flags).GetValue(d);
                var plugins =
                    (List<(IDalamudPlugin Plugin, PluginDefinition Definition, DalamudPluginInterface PluginInterface, bool IsRaw)>)
                    pmanager.GetType().GetProperty("Plugins").GetValue(pmanager);
                foreach (var p in plugins)
                {
                    if (p.Plugin.Name == "Visibility")
                    {
                        return p.Plugin;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                pi.Framework.Gui.Chat.Print("Can't find visibility plugin: " + e.Message);
                pi.Framework.Gui.Chat.Print(e.StackTrace);
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
    }
}
