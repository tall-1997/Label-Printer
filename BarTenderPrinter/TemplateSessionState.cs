using System.Collections.Generic;

namespace BarTenderPrinter
{
    public class TemplateSessionState
    {
        public int SchemaVersion { get; set; } = 3;
        public string Scope { get; set; } = "GlobalTemplate";
        public string OrderId { get; set; } = "";
        public string TemplateId { get; set; } = "";
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
        public List<string> TemplateFields { get; set; } = new List<string>();
        public List<string> LocalData { get; set; } = new List<string>();
        public List<DataSourceItem> DataSources { get; set; } = new List<DataSourceItem>();

        public TemplateSettings ToSettings()
        {
            return new TemplateSettings
            {
                SchemaVersion = SchemaVersion,
                Scope = Scope,
                OrderId = OrderId,
                TemplateId = TemplateId,
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
                TemplateFields = new List<string>(TemplateFields ?? new List<string>()),
                LocalData = new List<string>(LocalData ?? new List<string>()),
                DataSources = CloneDataSources(DataSources)
            };
        }

        public static TemplateSessionState FromSettings(TemplateSettings settings)
        {
            settings ??= new TemplateSettings();
            return new TemplateSessionState
            {
                SchemaVersion = settings.SchemaVersion,
                Scope = settings.Scope,
                OrderId = settings.OrderId,
                TemplateId = settings.TemplateId,
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
                TemplateFields = new List<string>(settings.TemplateFields ?? new List<string>()),
                LocalData = new List<string>(settings.LocalData ?? new List<string>()),
                DataSources = CloneDataSources(settings.DataSources)
            };
        }

        private static List<DataSourceItem> CloneDataSources(IEnumerable<DataSourceItem> sources)
        {
            var result = new List<DataSourceItem>();
            foreach (var source in sources ?? new List<DataSourceItem>())
            {
                result.Add(new DataSourceItem
                {
                    Name = source.Name,
                    Field = source.Field,
                    Enabled = source.Enabled,
                    AutoIncrement = source.AutoIncrement,
                    AutoStep = source.AutoStep,
                    IsLocked = source.IsLocked,
                    LockAfterInput = source.LockAfterInput,
                    LockedValue = source.LockedValue,
                    AutoIncrementLocked = source.AutoIncrementLocked,
                    ExpectedLength = source.ExpectedLength,
                    LengthRevision = source.LengthRevision,
                    LengthEdited = source.LengthEdited,
                    UseLocalDataValidation = source.UseLocalDataValidation
                });
            }
            return result;
        }
    }
}
