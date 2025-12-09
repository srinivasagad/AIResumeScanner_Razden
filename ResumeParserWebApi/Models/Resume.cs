using Newtonsoft.Json;

public class Resume
{
    [JsonProperty("full_name")]
    public string FullName { get; set; }

    [JsonProperty("email")]
    public string Email { get; set; }

    [JsonProperty("phone")]
    public string Phone { get; set; }

    [JsonProperty("location")]
    public string Location { get; set; }

    [JsonProperty("professional_summary")]
    public string ProfessionalSummary { get; set; }

    [JsonProperty("skills")]
    public List<string> Skills { get; set; }

    [JsonProperty("total_experience_years")]
    public double? TotalExperienceYears { get; set; }

    [JsonProperty("work_experience")]
    public List<WorkExperience> WorkExperience { get; set; }

    [JsonProperty("education")]
    public List<Education> Education { get; set; }

    [JsonProperty("certifications")]
    public List<Certification> Certifications { get; set; }

    [JsonProperty("projects")]
    public List<Project> Projects { get; set; }

    [JsonProperty("languages")]
    public List<string> Languages { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    public Resume()
    {
        Skills = new List<string>();
        WorkExperience = new List<WorkExperience>();
        Education = new List<Education>();
        Certifications = new List<Certification>();
        Projects = new List<Project>();
        Languages = new List<string>();
    }
}

public class WorkExperience
{
    [JsonProperty("job_title")]
    public string JobTitle { get; set; }

    [JsonProperty("company_name")]
    public string CompanyName { get; set; }

    [JsonProperty("start_date")]
    public string StartDate { get; set; }

    [JsonProperty("end_date")]
    public string EndDate { get; set; }

    [JsonProperty("location")]
    public string Location { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }
}

public class Education
{
    [JsonProperty("degree")]
    public string Degree { get; set; }

    [JsonProperty("field_of_study")]
    public string FieldOfStudy { get; set; }

    [JsonProperty("institution_name")]
    public string InstitutionName { get; set; }

    [JsonProperty("start_date")]
    public string StartDate { get; set; }

    [JsonProperty("end_date")]
    public string EndDate { get; set; }
}

public class Certification
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("issuer")]
    public string Issuer { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }
}

public class Project
{
    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("technologies")]
    public string Technologies { get; set; }
}
