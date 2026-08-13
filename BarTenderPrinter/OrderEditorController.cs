using System.Collections.Generic;
using System.Linq;

namespace BarTenderPrinter
{
    public class OrderEditorController
    {
        public List<OrderTemplate> CloneTemplates(IEnumerable<OrderTemplate> templates)
        {
            return (templates ?? new List<OrderTemplate>()).Select(template => new OrderTemplate
            {
                Id = template.Id,
                SourcePath = template.SourcePath,
                ArchivedPath = template.ArchivedPath,
                SourceLastWriteTimeUtcTicks = template.SourceLastWriteTimeUtcTicks,
                SourceLength = template.SourceLength,
                SourceSha256 = template.SourceSha256,
                FieldSnapshot = (template.FieldSnapshot ?? new List<string>()).ToList(),
                Settings = TemplateSessionState.FromSettings(template.Settings).ToSettings()
            }).ToList();
        }
    }
}
