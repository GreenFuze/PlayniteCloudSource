using CloudSource.Playnite.Providers.OneDrive;
using Playnite.SDK;
using System;
using System.Windows;

namespace CloudSource.Playnite
{
    internal sealed class OneDriveFolderPickerDialog
    {
        private readonly IDialogsFactory dialogs;
        private readonly IOneDriveFolderBrowser browser;

        public OneDriveFolderPickerDialog(IDialogsFactory dialogs, IOneDriveFolderBrowser browser)
        {
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
        }

        public OneDriveFolder Show(
            OneDriveAccountConfiguration configuration,
            OneDriveAuthorization draftAuthorization,
            string existingSelectionPath)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var window = dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
                ShowMinimizeButton = false,
                ShowCloseButton = true
            });
            window.Title = "Choose OneDrive source folder";
            window.Owner = dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Width = 680;
            window.Height = 560;
            window.MinWidth = 560;
            window.MinHeight = 440;

            OneDriveFolder result = null;
            var viewModel = new OneDriveFolderPickerViewModel(
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
            window.Content = new OneDriveFolderPickerView { DataContext = viewModel };
            window.Closed += (_, __) => viewModel.Dispose();
            window.ShowDialog();
            return result;
        }
    }
}
