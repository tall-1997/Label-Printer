using System.Collections.Generic;

namespace BarTenderPrinter
{
    public class TemplateSessionState
    {
        public int SchemaVersion { get; set; } = 2;
        public string TemplateName { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string Printer { get; set; } = "";
        public int Copies { get; set; } = 1;
        public bool InputValidation { get; set; }
        public bool DuplicateValidation { get; set; } = true;
        public bool LengthValidation { get; set; }
        public int GlobalExpectedLength { get; set; }
        public long GlobalLengthRevision { get; set; }
        public long LengthRevisionCounter { get; set; }
        public string LocalDataPath { get; set; } = "";
        public string LocalDataStoragePath { get; set; } = "";
        public string LocalDataColumnName { get; set; } = "";
        public string LocalDataTargetField { get; set; } = "";
        public List<string> LocalData { get; set; } = new List<string>();
        public List<DataSourceItem> DataSources { get; set; } = new List<DataSourceItem>();

        public TemplateSettings ToSettings()
        {
            return new TemplateSettings
            {
                SchemaVersion = SchemaVersion,
                TemplateName = TemplateName,
                TemplatePath = TemplatePath,
                Printer = Printer,
                Copies = Copies,
                InputValidation = InputValidation,
                DuplicateValidation = DuplicateValidation,
                LengthValidation = LengthValidation,
                GlobalExpectedLength = GlobalExpectedLength,
                GlobalLengthRevision = GlobalLengthRevision,
                LengthRevisionCounter = LengthRevisionCounter,
                LocalDataPath = LocalDataPath,
                LocalDataStoragePath = LocalDataStoragePath,
                LocalDataColumnName = LocalDataColumnName,
                LocalDataTargetField = LocalDataTargetField,
                LocalData = LocalData ?? new List<string>(),
                DataSources = DataSources ?? new List<DataSourceItem>()
            };
        }

        public static TemplateSessionState FromSettings(TemplateSettings settings)
        {
            settings ??= new TemplateSettings();
            return new TemplateSessionState
            {
                SchemaVersion = settings.SchemaVersion,
                TemplateName = settings.TemplateName,
                TemplatePath = settings.TemplatePath,
                Printer = settings.Printer,
                Copies = settings.Copies,
                InputValidation = settings.InputValidation,
                DuplicateValidation = settings.DuplicateValidation,
                LengthValidation = settings.LengthValidation,
                GlobalExpectedLength = settings.GlobalExpectedLength,
                GlobalLengthRevision = settings.GlobalLengthRevision,
                LengthRevisionCounter = settings.LengthRevisionCounter,
                LocalDataPath = settings.LocalDataPath,
                LocalDataStoragePath = settings.LocalDataStoragePath,
                LocalDataColumnName = settings.LocalDataColumnName,
                LocalDataTargetField = settings.LocalDataTargetField,
                LocalData = settings.LocalData ?? new List<string>(),
                DataSources = settings.DataSources ?? new List<DataSourceItem>()
            };
        }
    }
}
