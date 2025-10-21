using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Models
{
    public class ConversationMessage
    {
        public DateTime Timestamp { get; set; }
        public string Role { get; set; }
        public string Content { get; set; }
    }
}
