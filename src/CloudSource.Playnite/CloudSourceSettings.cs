using CloudSource.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace CloudSource.Playnite
{
    public sealed class CloudSourceSettings : ObservableObject
    {
        private string managedRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Cloud Source");

        public string ManagedRootPath
        {
            get => managedRootPath;
            set => SetValue(ref managedRootPath, value);
        }
    }

    public sealed class CloudSourceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CloudSourcePlugin plugin;
        private CloudSourceSettings editingClone;
        private CloudSourceSettings settings;

        public CloudSourceSettings Settings
        {
            get => settings;
            private set => SetValue(ref settings, value);
        }

        public CloudSourceSettingsViewModel(CloudSourcePlugin plugin)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            Settings = plugin.LoadPluginSettings<CloudSourceSettings>() ?? new CloudSourceSettings();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new CloudSourceSettings();
        }

        public void EndEdit()
        {
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out var layout, out var error))
            {
                throw new InvalidOperationException(error);
            }

            layout.EnsureCreated();
            Settings.ManagedRootPath = layout.RootPath;
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!ManagedStorageLayout.TryCreate(Settings.ManagedRootPath, out _, out var error))
            {
                errors.Add(error);
            }

            return errors.Count == 0;
        }
    }
}
