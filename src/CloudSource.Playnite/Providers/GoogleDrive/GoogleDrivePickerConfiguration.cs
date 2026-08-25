using System;

namespace CloudSource.Playnite.Providers.GoogleDrive
{
    internal sealed class GoogleDrivePickerConfiguration
    {
        public string ProjectNumber { get; }
        public string ApiKey { get; }

        public GoogleDrivePickerConfiguration(string projectNumber, string apiKey)
        {
            ProjectNumber = Required(projectNumber, nameof(projectNumber));
            ApiKey = Required(apiKey, nameof(apiKey));
        }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", parameterName);
            return value.Trim();
        }
    }
}
