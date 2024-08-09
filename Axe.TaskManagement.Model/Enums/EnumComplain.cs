using System.ComponentModel;

namespace Axe.TaskManagement.Model.Enums
{
    public class EnumComplain
    {
        public enum Status
        {
            [Description("Lưu tạm")]
            Draft,
            [Description("Chưa xử lý")]
            Unprocessed,
            [Description("Đang xử lý")]
            Processing,
            [Description("Hoàn thành")]
            Complete
        }
        public enum RightStatus
        {
            [Description("Chờ confirm")]
            WaitingConfirm,
            [Description("Sai")]
            Wrong,
            [Description("Đúng")]
            Correct
        }
        public enum ChooseValue
        {
            [Description("Dữ liệu đã nhập")]
            Before = 1,
            [Description("Dữ liệu của hệ thống")]
            Compare = 2,
            [Description("Khác")]
            Other = 3
        }
    }
}
