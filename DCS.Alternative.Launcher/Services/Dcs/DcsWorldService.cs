﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DCS.Alternative.Launcher.Diagnostics.Trace;
using DCS.Alternative.Launcher.Models;
using DCS.Alternative.Launcher.Modules;
using DCS.Alternative.Launcher.ServiceModel;
using DCS.Alternative.Launcher.ServiceModel.Syndication;
using HtmlAgilityPack;
using NLua;

namespace DCS.Alternative.Launcher.Services.Dcs
{
    public class DcsWorldService : IDcsWorldService
    {
        private readonly IContainer _container;

        public DcsWorldService(IContainer container)
        {
            _container = container;
        }

        public Task<Module[]> GetInstalledAircraftModulesAsync()
        {
#pragma warning disable CS1998 // This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
            return Task.Run(async () =>
#pragma warning restore CS1998 // This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
            {
                var settingsService = _container.Resolve<ISettingsService>();
                var modules = new List<Module>();
                var install = settingsService.SelectedInstall;
                var autoupdateModules = install.Modules;

                if (!install.IsValidInstall)
                {
                    return modules.ToArray();
                }

                var aircraftFolders = Directory.GetDirectories(Path.Combine(install.Directory, "Mods//aircraft"));

                foreach (var folder in aircraftFolders)
                {
                    using (var lua = new Lua())
                    {
                        lua.State.Encoding = Encoding.UTF8;

                        var entryPath = Path.Combine(folder, "entry.lua");

                        lua.DoString(
                            @"function _(s) return s end
                                function _(s) return s end
                                function mount_vfs_liveries_path() end
                                function mount_vfs_texture_path() end
                                function mount_vfs_sound_path() end
                                function mount_vfs_model_path() end
                                function make_view_settings() end
                                function set_manual_path() end
                                function dofile() end
                                function plugin_done() end
                                function make_flyable() end
                                AV8BFM = {}
                                F86FM = {}
                                F5E = {}
                                FA18C = {}
                                F15FM = {}
                                FM = {}
                                M2KFM = {}
                                Mig15FM = {}
                                MIG19PFM = {}
                                SA342FM = {}
                                " + $"__DCS_VERSION__ = \"{install.Version}\"");


                        lua.DoString($"current_mod_path = \"{folder.Replace("\\", "\\\\")}\"");

                        var moduleId = string.Empty;
                        var skinsPath = string.Empty;

                        lua["declare_plugin"] = new Action<string, LuaTable>((id, description) =>
                        {
                            if (description.Keys.OfType<string>().All(k => k != "update_id"))
                            {
                                return;
                            }

                            moduleId = description["update_id"].ToString();
                            skinsPath = ((LuaTable)((LuaTable)description["Skins"])[1])["dir"].ToString();
                        });

                        lua["make_flyable"] = new Action<string, string, LuaTable, string>((displayName, b, c, d) =>
                        {
                            if (!string.IsNullOrEmpty(moduleId) && autoupdateModules.Contains(moduleId) && moduleId != "FC3")
                            {
                                var module = new Module()
                                {
                                    ModuleId = moduleId,
                                    DisplayName = displayName,
                                    BaseFolderPath = folder,
                                    IconPath = Path.Combine(folder, skinsPath, "icon.png"),
                                    ViewportPrefix = moduleId.ToString().Replace(" ", "_").Replace("-", "_")
                                };
                                modules.Add(module);
                            }
                        });

                        try
                        {
                            lua.DoFile(entryPath);
                        }
                        catch (Exception e)
                        {
                            Tracer.Error(e);
                        }
                    }
                }

                return modules.ToArray();
            });
        }

        public Task<ReadOnlyDictionary<string, Version>> GetLatestVersionsAsync()
        {
            return Task.Run(async () =>
            {
                using (var client = new HttpClient())
                {
                    var html = await client.GetStringAsync("http://updates.digitalcombatsimulator.com/");
                    var doc = new HtmlDocument();

                    doc.LoadHtml(html);

                    var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'well')]").ToArray();
                    var node = nodes.FirstOrDefault();

                    var versions = new Dictionary<string, Version>();

                    if (node != null)
                    {
                        foreach (var h2 in node.SelectNodes("h2"))
                        {
                            var innerText = h2.InnerText;
                            var split = innerText.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                            var version = Version.Parse(split.LastOrDefault() ?? string.Empty);

                            versions.Add(innerText.ToLower().Contains("stable") ? "stable" : "openbeta", version);
                        }
                    }

                    return new ReadOnlyDictionary<string, Version>(versions);
                }
            });
        }

        public Task<NewsArticleModel[]> GetLatestNewsArticlesAsync(int count = 10)
        {
            return Task.Run(async () =>
            {
                var articles = new List<NewsArticleModel>();

                using (var client = new HttpClient())
                {
                    var html = await client.GetStringAsync("https://www.digitalcombatsimulator.com/en/news/");
                    var doc = new HtmlDocument();

                    doc.LoadHtml(html);

                    var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'well')]").ToArray();

                    foreach (var node in nodes.Take(count))
                    {
                        if (!node.Id.StartsWith("bx_"))
                        {
                            continue;
                        }

                        var divs = node.SelectNodes("div");
                        var article = new NewsArticleModel();
                        var dayMonth = divs[0].SelectSingleNode("div[1]/div[1]").InnerText.Trim();
                        var year = divs[0].SelectSingleNode("div[1]/div[2]").InnerText.Trim();
                        var title = divs[0].SelectSingleNode("div[2]/div[1]/h3[1]/a[1]").InnerText.Trim();
                        var summary = divs[0].SelectSingleNode("div[2]/div[2]/div[1]").InnerText.Trim();
                        var url = "https://www.digitalcombatsimulator.com" +
                                  divs[0].SelectSingleNode("div[2]/a[1]").Attributes["href"].Value.Trim();

                        article.Title.Value = title;
                        article.Summary.Value = summary;
                        article.Url.Value = url;
                        article.Day.Value = dayMonth;
                        article.Year.Value = year;
                        article.ImageSource.Value =
                            $"/Images/Backgrounds/background ({Convert.ToInt32(article.Day.Value.Substring(0, dayMonth.Length - 3).Trim()) % 20 + 1}).jpg";

                        articles.Add(article);
                    }

                    return articles.ToArray();
                }
            });
        }

        public Task<string> GetLatestYoutubeVideoUrlAsync()
        {
            return Task.Run(async () =>
            {
                var feed = await SyndicationHelper.GetFeedAsync(
                    "https://www.youtube.com/feeds/videos.xml?channel_id=UCgJRhtnqA-67pKmQ3A2GsgA");
                var latestFeed = feed.Items.OrderByDescending(i => i.PublishDate).FirstOrDefault();

                if (latestFeed != null)
                {
                    var link = latestFeed.Links[0].Uri.ToString();
                    return link.Replace("watch?v=", "embed/") + "?autoplay=1&rel=0";
                }

                return string.Empty;
            });
        }
    }
}