using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace BarTenderPrinter
{
    public static class HistoryPresenter
    {
        public static DataTable BuildTable(IEnumerable<PrintRecord> records)
        {
            var table = new DataTable();
            table.Columns.Add("记录ID");
            table.Columns.Add("数据");
            table.Columns.Add("打印时间");
            table.Columns.Add("状态");
            table.Columns.Add("打印机");
            table.Columns.Add("份数");
            foreach (var record in records ?? new List<PrintRecord>())
            {
                var values = record.FieldValues != null && record.FieldValues.Count > 0
                    ? string.Join(" | ", record.FieldValues.Select(item => $"{item.Key}={item.Value}"))
                    : record.Imei;
                table.Rows.Add(record.RecordId, values, record.PrintTime, record.Status, record.Printer, record.Copies);
            }
            return table;
        }
    }
}
