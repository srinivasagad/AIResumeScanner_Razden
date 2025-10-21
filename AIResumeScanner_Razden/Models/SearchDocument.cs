using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Models
{
    public class SearchDocument
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public string Title { get; set; }
        public float[]? Embedding { get; set; }
    }
}
