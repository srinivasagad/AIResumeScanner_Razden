using System.ComponentModel.DataAnnotations;
namespace AIResumeScanner_Razden.Models
{
    public class JobModel
    {
        [Required]
        public string? JobDescription { get; set; }
    }
}
