namespace ResumeParserWebApi.Models
{
    public class SearchFilterRequest
    {
        // Simple fields
        public string Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Location { get; set; }
        public string ProfessionalSummary { get; set; }
        public string FileName { get; set; }
        public string ResumeUrl { get; set; }
        public string ResumeId { get; set; }
        public bool? Availability { get; set; }
        public bool? IsActive { get; set; }

        // Skills and languages (collections)
        public List<string> Skills { get; set; }
        public List<string> Languages { get; set; }
        public List<string> Certifications { get; set; }

        // Experience range
        public double? MinTotalExperienceYears { get; set; }
        public double? MaxTotalExperienceYears { get; set; }

        // Upload date range
        public DateTimeOffset? MinUploadDate { get; set; }
        public DateTimeOffset? MaxUploadDate { get; set; }

        // Nested/complex fields for work experience
        public List<WorkExperienceFilter> WorkExperience { get; set; }

        // Nested/complex fields for education
        public List<EducationFilter> Education { get; set; }

        // Nested/complex fields for projects
        public List<ProjectFilter> Projects { get; set; }
    }

    // Nested filter classes for complex fields
    public class WorkExperienceFilter
    {
        public string JobTitle { get; set; }
        public string CompanyName { get; set; }
        public string StartDate { get; set; } // Use string or DateTime depending on your data
        public string EndDate { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
    }

    public class EducationFilter
    {
        public string Degree { get; set; }
        public string FieldOfStudy { get; set; }
        public string InstitutionName { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
    }

    public class ProjectFilter
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> Technologies { get; set; }
    }

    public class SearchRequest
    {
        public string search { get; set; }
        public string filter { get; set; }
        public bool count { get; set; }
        public string select { get; set; }
    }
    public class SearchResponse
    {
        public List<ResumeDocument> value { get; set; }
    }
    public class ResumeDocument
    {
        public string full_name { get; set; }
        public string phone { get; set; }
        public string email { get; set; }
        public string resume_url { get; set; }
        public string file_name { get; set; }
    }

}
