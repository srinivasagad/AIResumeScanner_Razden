using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Models
{
    public class ConversationState
    {
        public string SessionId { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}
