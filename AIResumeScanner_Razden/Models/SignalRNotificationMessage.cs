namespace AIResumeScanner_Razden.Models
{
    public  class SignalRNotificationMessage
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public object Data { get; set; }
    }
}
