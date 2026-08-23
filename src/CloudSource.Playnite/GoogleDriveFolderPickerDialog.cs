using CloudSource.Playnite.Providers.GoogleDrive;
using Playnite.SDK;
using System;
using System.Windows;

namespace CloudSource.Playnite
{
    internal sealed class GoogleDriveFolderPickerDialog
    {
        private readonly IDialogsFactory dialogs;
        private readonly IGoogleDriveFolderBrowser browser;

        public GoogleDriveFolderPickerDialog(
            IDialogsFactory dialogs,
            IGoogleDriveFolderBrowser browser)
        {
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
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

            var window = dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
                ShowMinimizeButton = false,
                ShowCloseButton = true
            });
            window.Title = "Choose Google Drive source folder";
            window.Owner = dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Width = 680;
            window.Height = 560;
            window.MinWidth = 560;
            window.MinHeight = 440;

            GoogleDriveFolder result = null;
            GoogleDriveFolderPickerViewModel viewModel = null;
            viewModel = new GoogleDriveFolderPickerViewModel(
                browser,
                configuration,
                draftAuthorization,
                existingSelectionPath,
                folder =>
                {
                    result = folder;
                    window.DialogResult = true;
                },
                () => window.DialogResult = false,
                message => dialogs.ShowErrorMessage(message, CloudStorageProduct.DisplayName));
            window.Content = new GoogleDriveFolderPickerView
            {
                DataContext = viewModel
            };
            window.Closed += (_, __) => viewModel.Dispose();
            window.ShowDialog();
            return result;
        }
    }
}
