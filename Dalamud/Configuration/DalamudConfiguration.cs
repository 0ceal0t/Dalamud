using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Configuration;
using Dalamud.DiscordBot;
using Newtonsoft.Json;

namespace Dalamud
{
    [Serializable]
    public class DalamudConfiguration
    {
        public DiscordFeatureConfiguration DiscordFeatureConfig { get; set; }

        public bool OptOutMbCollection { get; set; } = false;

        public List<string> BadWords { get; set; }

        public enum PreferredRole
        {
            None,
            All,
            Tank,
            Dps,
            Healer
        }

        public Dictionary<int, PreferredRole> PreferredRoleReminders { get; set; }

        public string LanguageOverride { get; set; }

        public string LastVersion { get; set; }

        public class ConfigPlugin
        {
            public bool IsEnabled { get; set; }
            public string InternalName { get; set; }
        }

        public List<ConfigPlugin> InstalledPlugins { get; set; }

        [JsonIgnore]
        public string ConfigPath;

        public static DalamudConfiguration Load(string path) {
            var deserialized = JsonConvert.DeserializeObject<DalamudConfiguration>(File.ReadAllText(path));
            deserialized.ConfigPath = path;

            return deserialized;
        }

        /// <summary>
        /// Save the configuration at the path it was loaded from.
        /// </summary>
        public void Save() {
            File.WriteAllText(this.ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
