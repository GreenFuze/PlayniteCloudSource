using CloudSource.Playnite.Providers.GoogleDrive;
using Playnite.SDK;
using System;

namespace CloudSource.Playnite
{
    internal sealed class GoogleDriveFolderPickerDialog
    {
        private readonly IDialogsFactory dialogs;
        private readonly GoogleDrivePickerClient pickerClient;

        public GoogleDriveFolderPickerDialog(
            IDialogsFactory dialogs,
            GoogleDrivePickerClient pickerClient)
        {
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.pickerClient = pickerClient ?? throw new ArgumentNullException(nameof(pickerClient));
        }

        public GoogleDriveFolder Show(
            GoogleDriveAccountConfiguration configuration,
            GoogleDriveAuthorization draftAuthorization,
            string existingSelectionPath)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            GoogleDriveFolder result = null;
            var progress = dialogs.ActivateGlobalProgress(
                async args =>
                {
                    result = await pickerClient.SelectFolderAsync(
                        configuration,
                        draftAuthorization,
                        args.CancelToken);
                },
                new GlobalProgressOptions("Choose a Google Drive source folder in your browser", true)
                {
                    IsIndeterminate = true
                });
            if (progress.Error != null)
            {
                throw progress.Error;
            }

            if (progress.Canceled) return null;
            return result;
        }
    }
}
