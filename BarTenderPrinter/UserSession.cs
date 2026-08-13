namespace BarTenderPrinter
{
    public class UserSession
    {
        public string OperatorName { get; set; } = "";
        public string Role { get; set; } = "Admin";

        public bool CanDeleteHistory => Role == "Admin";
        public bool CanApproveReprint => Role == "Admin" || Role == "Supervisor";
    }
}
