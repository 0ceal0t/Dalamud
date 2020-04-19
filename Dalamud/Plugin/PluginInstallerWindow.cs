using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using CheapLoc;
using ImGuiNET;
using Newtonsoft.Json;
using Serilog;

namespace Dalamud.Plugin
{
    class PluginInstallerWindow {
        private const string PluginRepoBaseUrl = "https://goaaats.github.io/DalamudPlugins/";

        private readonly Dalamud dalamud;
        private string gameVersion;

        private bool errorModalDrawing = true;
        private bool errorModalOnNextFrame = false;

        private bool updateComplete = false;
        private int updatePluginCount = 0;

        private enum PluginInstallStatus {
            None,
            InProgress,
            Success,
            Fail
        }

        private PluginInstallStatus installStatus = PluginInstallStatus.None;

        public PluginInstallerWindow(Dalamud dalamud, string gameVersion) {
            this.dalamud = dalamud;
            this.gameVersion = gameVersion;
        }

        public bool Draw() {
            var windowOpen = true;

            ImGui.SetNextWindowSize(new Vector2(750, 520));

            ImGui.Begin(Loc.Localize("InstallerHeader", "Plugin Installer"), ref windowOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar);

            ImGui.Text(Loc.Localize("InstallerHint", "This window allows you install and remove in-game plugins.\nThey are made by third-party developers."));
            ImGui.Separator();

            ImGui.BeginChild("scrolling", new Vector2(0, 400), true, ImGuiWindowFlags.HorizontalScrollbar);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1, 3));

            var installedPlugins = this.dalamud.Configuration.InstalledPlugins;

            if (installedPlugins.Count != 0)
            {
                ImGui.TextColored(new Vector4(0.86f, 0.86f, 0.86f, 1.00f), Loc.Localize("InstallerInstalledHint", "Installed Plugins"));
                ImGui.Dummy(new Vector2(5f, 5f));

                foreach (var installedPlugin in installedPlugins)
                {
                    var loadedPlugin = this.dalamud.PluginManager.GetLoadedPlugin(installedPlugin.InternalName);

                    if (loadedPlugin == null)
                        continue;

                    ImGui.PushID(installedPlugin.InternalName + "InstalledHeader");

                    var loadState = Loc.Localize("InstallerEnabledHint", " (enabled)");
                    switch (loadedPlugin.LoadState)
                    {
                        case PluginManager.PluginLoadState.NotApplicable:
                            loadState = Loc.Localize("InstallerOutOfDateHint", " (out of date)");
                            break;
                        case PluginManager.PluginLoadState.InitFailed:
                            loadState = Loc.Localize("InstallerLoadFailedHint", " (load failed)");
                            break;
                        case PluginManager.PluginLoadState.Disabled:
                            loadState = Loc.Localize("InstallerDisabledHint", " (disabled)");
                            break;
                    }

                    var remoteDef =
                        this.dalamud.PluginRepository.PluginMaster.FirstOrDefault(
                            x => x.InternalName == installedPlugin.InternalName);
                    var needsUpdate = remoteDef != null &&
                                      remoteDef.AssemblyVersion != loadedPlugin.Definition.AssemblyVersion;

                    if (needsUpdate)
                        loadState = Loc.Localize("InstallerDisabledHint", " (needs update)");

                    if (ImGui.CollapsingHeader(loadedPlugin.Definition.Name + loadState))
                    {
                        ImGui.Indent();

                        ImGui.Text(loadedPlugin.Definition.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $" by {loadedPlugin.Definition.Author}");

                        ImGui.Text(loadedPlugin.Definition.Description);

                        if (!installedPlugin.IsEnabled)
                        {
                            if (this.installStatus == PluginInstallStatus.InProgress)
                            {
                                ImGui.Button(Loc.Localize("InstallerInProgress", "Install in progress..."));
                            }
                            else
                            {
                                if (ImGui.Button(string.Format(Loc.Localize("InstallerEnable", "Enable v{0}"), loadedPlugin.Definition.AssemblyVersion)))
                                {
                                    this.installStatus = PluginInstallStatus.InProgress;

                                    Task.Run(() => this.dalamud.PluginRepository.InstallPlugin(loadedPlugin.Definition.InternalName)).ContinueWith(t => {
                                        this.installStatus =
                                            t.Result ? PluginInstallStatus.Success : PluginInstallStatus.Fail;
                                        this.installStatus =
                                            t.IsFaulted ? PluginInstallStatus.Fail : this.installStatus;

                                        this.errorModalDrawing = this.installStatus == PluginInstallStatus.Fail;
                                        this.errorModalOnNextFrame = this.installStatus == PluginInstallStatus.Fail;
                                    });
                                }
                            }
                        }
                        else
                        {
                            if (ImGui.Button(Loc.Localize("InstallerDisable", "Disable")))
                                try
                                {
                                    this.dalamud.PluginRepository.DisablePlugin(loadedPlugin.Definition.InternalName);
                                }
                                catch (Exception exception)
                                {
                                    Log.Error(exception, "Could not disable plugin.");
                                    this.errorModalDrawing = true;
                                    this.errorModalOnNextFrame = true;
                                }

                            if (loadedPlugin.PluginInterface.UiBuilder.OnOpenConfigUi != null)
                            {
                                ImGui.SameLine();

                                if (ImGui.Button(Loc.Localize("InstallerOpenConfig", "Open Configuration"))) loadedPlugin.PluginInterface.UiBuilder.OnOpenConfigUi?.Invoke(null, null);
                            }

                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $" v{loadedPlugin.Definition.AssemblyVersion}");
                        }

                        ImGui.Unindent();
                    }

                    ImGui.PopID();
                }

                ImGui.Dummy(new Vector2(10f, 10f));

                ImGui.Separator();
            }

            ImGui.TextColored(new Vector4(0.86f, 0.86f, 0.86f, 1.00f), Loc.Localize("InstallerAvailableHint", "Available Plugins"));
            ImGui.Dummy(new Vector2(5f, 5f));

            if (this.dalamud.PluginRepository.State == PluginRepository.InitializationState.InProgress) {
                ImGui.Text(Loc.Localize("InstallerLoading", "Loading plugins..."));
            } else if (this.dalamud.PluginRepository.State == PluginRepository.InitializationState.Fail) {
                ImGui.Text(Loc.Localize("InstallerDownloadFailed", "Download failed."));
            }
            else
            {
                foreach (var pluginDefinition in this.dalamud.PluginRepository.PluginMaster) {
                    // Skip plugins we already listed in the "installed" part
                    if (installedPlugins.Any(x => x.InternalName == pluginDefinition.InternalName))
                        continue;

                    if (pluginDefinition.ApplicableVersion != this.gameVersion &&
                        pluginDefinition.ApplicableVersion != "any")
                        continue;

                    if (pluginDefinition.IsHide)
                        continue;

                    ImGui.PushID(pluginDefinition.InternalName + pluginDefinition.AssemblyVersion);

                    var isInstalled = this.dalamud.PluginManager.Plugins.Where(x => x.Definition != null).Any(
                        x => x.Definition.InternalName == pluginDefinition.InternalName);

                    if (ImGui.CollapsingHeader(pluginDefinition.Name + (isInstalled ? Loc.Localize("InstallerInstalled", " (installed)") : string.Empty))) {
                        ImGui.Indent();

                        ImGui.Text(pluginDefinition.Name);
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $" by {pluginDefinition.Author}");

                        ImGui.Text(pluginDefinition.Description);

                        if (!isInstalled) {
                            if (this.installStatus == PluginInstallStatus.InProgress) {
                                ImGui.Button(Loc.Localize("InstallerInProgress", "Install in progress..."));
                            } else {
                                if (ImGui.Button($"Install v{pluginDefinition.AssemblyVersion}")) {
                                    this.installStatus = PluginInstallStatus.InProgress;

                                    Task.Run(() => this.dalamud.PluginRepository.InstallPlugin(pluginDefinition.InternalName)).ContinueWith(t => {
                                        this.installStatus =
                                            t.Result ? PluginInstallStatus.Success : PluginInstallStatus.Fail;
                                        this.installStatus =
                                            t.IsFaulted ? PluginInstallStatus.Fail : this.installStatus;

                                        this.errorModalDrawing = this.installStatus == PluginInstallStatus.Fail;
                                        this.errorModalOnNextFrame = this.installStatus == PluginInstallStatus.Fail;
                                    });
                                }
                            }
                        } else {
                            var installedPlugin = this.dalamud.PluginManager.Plugins.Where(x => x.Definition != null).First(
                                x => x.Definition.InternalName ==
                                     pluginDefinition.InternalName);

                            if (ImGui.Button(Loc.Localize("InstallerDisable", "Disable")))
                                try {
                                    this.dalamud.PluginRepository.DisablePlugin(installedPlugin.Definition.InternalName);
                                } catch (Exception exception) {
                                    Log.Error(exception, "Could not disable plugin.");
                                    this.errorModalDrawing = true;
                                    this.errorModalOnNextFrame = true;
                                }

                            if (installedPlugin.PluginInterface.UiBuilder.OnOpenConfigUi != null) {
                                ImGui.SameLine();

                                if (ImGui.Button(Loc.Localize("InstallerOpenConfig", "Open Configuration"))) installedPlugin.PluginInterface.UiBuilder.OnOpenConfigUi?.Invoke(null, null);
                            }

                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $" v{pluginDefinition.AssemblyVersion}");
                        }

                        ImGui.Unindent();
                    }

                    ImGui.PopID();
                }
            }

            ImGui.PopStyleVar();

            ImGui.EndChild();

            ImGui.Separator();

            if (this.installStatus == PluginInstallStatus.InProgress) {
                ImGui.Button(Loc.Localize("InstallerUpdating", "Updating..."));
            } else {
                if (this.updateComplete) {
                    ImGui.Button(this.updatePluginCount == 0
                                     ? Loc.Localize("InstallerNoUpdates", "No updates found!")
                                     : string.Format(Loc.Localize("InstallerUpdateComplete", "{0} plugins updated!"), this.updatePluginCount));
                } else {
                    if (ImGui.Button(Loc.Localize("InstallerUpdatePlugins", "Update plugins")))
                    {
                        this.installStatus = PluginInstallStatus.InProgress;

                        Task.Run(() => this.dalamud.PluginRepository.UpdatePlugins()).ContinueWith(t => {
                            this.installStatus =
                                t.Result.Success ? PluginInstallStatus.Success : PluginInstallStatus.Fail;
                            this.installStatus =
                                t.IsFaulted ? PluginInstallStatus.Fail : this.installStatus;

                            if (this.installStatus == PluginInstallStatus.Success) {
                                this.updateComplete = true;
                                this.updatePluginCount = t.Result.UpdatedCount;
                            }

                            this.errorModalDrawing = this.installStatus == PluginInstallStatus.Fail;
                            this.errorModalOnNextFrame = this.installStatus == PluginInstallStatus.Fail;
                        });
                    }
                }
            }

            ImGui.SameLine();

            if (ImGui.Button(Loc.Localize("Close", "Close")))
            {
                windowOpen = false;
            }

            ImGui.Spacing();

            if (ImGui.BeginPopupModal(Loc.Localize("InstallerError","Installer failed"), ref this.errorModalDrawing, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text(Loc.Localize("InstallerErrorHint", "The plugin installer ran into an issue or the plugin is incompatible.\nPlease restart the game and report this error on our discord."));

                ImGui.Spacing();

                if (ImGui.Button(Loc.Localize("OK", "OK"), new Vector2(120, 40))) { ImGui.CloseCurrentPopup(); }

                ImGui.EndPopup();
            }

            if (this.errorModalOnNextFrame) {
                ImGui.OpenPopup(Loc.Localize("InstallerError", "Installer failed"));
                this.errorModalOnNextFrame = false;
            }
            
            ImGui.End();

            return windowOpen;
        }
    }
}
