namespace HarveyStressMeter.Models
{
    public sealed class PendingMedicalLetter
    {
        public string MailId { get; set; } = "";
        public string Reason { get; set; } = "";
        public string StateId { get; set; } = "";
        public int CreatedDay { get; set; }
        public int DeliverAfterDay { get; set; }
        public bool Critical { get; set; }
        public string DedupeKey { get; set; } = "";
    }
}
