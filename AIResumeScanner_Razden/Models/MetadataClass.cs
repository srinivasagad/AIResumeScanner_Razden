namespace AIResumeScanner_Razden.Models
{
    public class Education
    {
        public string degree { get; set; }
        public string field_of_study { get; set; }
        public string institution_name { get; set; }
        public object start_date { get; set; }
        public string end_date { get; set; }
    }

    public class Project
    {
        public string title { get; set; }
        public string description { get; set; }
        public List<string> technologies { get; set; }
    }

    public class WorkExperience
    {
        public string job_title { get; set; }
        public string company_name { get; set; }
        public string start_date { get; set; }
        public string end_date { get; set; }
        public string location { get; set; }
        public string description { get; set; }
    }

    public class MetadataClass
    {
        public string full_name { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
        public string location { get; set; }
        public string professional_summary { get; set; }
        public List<string> skills { get; set; }
        public int total_experience_years { get; set; }
        public List<WorkExperience> work_experience { get; set; }
        public List<Education> education { get; set; }
        public List<object> certifications { get; set; }
        public List<Project> projects { get; set; }
        public List<string> languages { get; set; }

    }
}
