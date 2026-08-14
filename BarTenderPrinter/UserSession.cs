namespace BarTenderPrinter
{
    public class UserSession
    {
        public string OperatorName { get; set; } = "";
        public string Role { get; set; } = "Operator";
        public bool IsAuthenticated { get; set; }

        public bool CanDeleteHistory => IsAuthenticated && Role == "Admin";
        public bool CanApproveReprint => IsAuthenticated && (Role == "Admin" || Role == "Supervisor");
    }
}
