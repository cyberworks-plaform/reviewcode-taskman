using System.ComponentModel;

namespace Axe.TaskManagement.Model.Enums
{
    public class EnumExportData
    {
        public enum Status
        {
            [Description("Tạo mới")]
            New = 1,
            [Description("Đang xử lý")]
            Processing = 2,
            [Description("Hoàn thành")]
            Complete = 3,
            [Description("Lỗi")]
            Error = 4
        }

        public enum Type
        {
            [Description("Excel")]
            Excel = 1,
            [Description("Csv")]
            Csv = 2,
            [Description("Xml")]
            Xml = 3,
            [Description("HistoryUserProcess")]
            HistoryUserProcess = 4
        }
    }
}
