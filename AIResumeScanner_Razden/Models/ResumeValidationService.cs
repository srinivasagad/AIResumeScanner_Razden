using Azure;
using Azure.AI.TextAnalytics;
using System.Text.RegularExpressions;

public class ResumeValidationService
{
    private readonly TextAnalyticsClient _client;

    public ResumeValidationService(string endpoint, string apiKey)
    {
        _client = new TextAnalyticsClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    }

    public async Task<ValidationResult> ValidateResumeDocument(string documentText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(documentText) || documentText.Length < 100)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Document is too short or empty to be a valid resume.",
                    Confidence = 0,
                    DocumentType = "Unknown"
                };
            }

            // STEP 1: Check for EXCLUSION patterns (non-resume documents)
            var exclusionResult = CheckExclusionPatterns(documentText);
            if (exclusionResult.IsExcluded)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = $"Document appears to be a {exclusionResult.DocumentType}, not a resume.",
                    Confidence = 0,
                    DocumentType = exclusionResult.DocumentType,
                    Reason = exclusionResult.Reason
                };
            }

            // STEP 2: Extract and analyze key phrases using Azure Text Analytics
            var keyPhrasesResponse = await _client.ExtractKeyPhrasesAsync(documentText);
            var keyPhrases = keyPhrasesResponse.Value.ToList();

            // STEP 3: Check for REQUIRED resume sections (must have at least 2 of these)
            var requiredSections = CheckRequiredResumeSections(documentText, keyPhrases);
            
            // STEP 4: Check for professional experience indicators
            var experienceIndicators = CheckExperienceIndicators(documentText);

            // STEP 5: Check for education indicators
            var educationIndicators = CheckEducationIndicators(documentText);

            // STEP 6: Check for skills section
            var skillsIndicators = CheckSkillsIndicators(documentText, keyPhrases);

            // STEP 7: Analyze document structure
            var structureScore = AnalyzeResumeStructure(documentText);

            // STEP 8: Check for career-related keywords (not just generic keywords)
            var careerKeywords = CheckCareerKeywords(keyPhrases);

            // Calculate final score
            int totalScore = 0;
            
            // Required sections (50 points max - critical for resume)
            totalScore += requiredSections.Count * 25;
            
            // Experience indicators (20 points)
            totalScore += experienceIndicators ? 20 : 0;
            
            // Education indicators (20 points)
            totalScore += educationIndicators ? 20 : 0;
            
            // Skills indicators (15 points)
            totalScore += skillsIndicators ? 15 : 0;
            
            // Structure score (15 points)
            totalScore += structureScore;
            
            // Career keywords (10 points)
            totalScore += careerKeywords > 3 ? 10 : (careerKeywords * 3);

            double confidence = Math.Min(100, totalScore);

            // Resume must have at least 2 required sections AND score > 60
            bool isValid = requiredSections.Count >= 2 && confidence >= 60;

            string detailedFeedback = GenerateDetailedFeedback(
                requiredSections, 
                experienceIndicators, 
                educationIndicators, 
                skillsIndicators,
                careerKeywords
            );

            return new ValidationResult
            {
                IsValid = isValid,
                Confidence = confidence,
                Message = isValid 
                    ? $"Document is a valid resume (Confidence: {confidence:F1}%)"
                    : $"Document does not appear to be a resume (Confidence: {confidence:F1}%). {detailedFeedback}",
                DocumentType = isValid ? "Resume/CV" : "Other Document",
                MatchedSections = requiredSections,
                Reason = detailedFeedback
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = $"Error validating document: {ex.Message}",
                Confidence = 0,
                DocumentType = "Error"
            };
        }
    }

    private (bool IsExcluded, string DocumentType, string Reason) CheckExclusionPatterns(string text)
    {
        var textLower = text.ToLower();

        // Common non-resume document indicators - any of these patterns suggest it's NOT a resume
        var nonResumePatterns = new[]
        {
            // Government/Official forms
            "registering authority", "registration certificate", "motor vehicle",
            "transport department", "rta office", "challan", "vehicle number",
            "chassis no", "engine no", "pollution certificate", "form - ",
            
            // Financial documents
            "invoice number", "bill number", "gstin", "gst number", 
            "purchase order", "quotation", "payment terms", "amount payable",
            
            // Legal/Contractual
            "whereas", "hereby", "witnesseth", "hereinafter", 
            "agreement entered", "terms and conditions", "legal notice",
            
            // Medical
            "patient name", "medical record", "prescription", "diagnosis",
            "lab report", "test results",
            
            // Administrative
            "applicant name", "application number", "reference number",
            "acknowledgement receipt", "slot booking", "appointment date"
        };

        int nonResumeMatches = nonResumePatterns.Count(p => textLower.Contains(p));
        
        // If document has 3 or more non-resume indicators, it's likely not a resume
        if (nonResumeMatches >= 3)
        {
            return (true, "Non-Resume Document", 
                $"Document contains administrative/official content not typical of resumes. Found {nonResumeMatches} non-resume indicators.");
        }

        return (false, string.Empty, string.Empty);
    }

    private List<string> CheckRequiredResumeSections(string text, List<string> keyPhrases)
    {
        var sections = new List<string>();
        var textLower = text.ToLower();

        // Professional Experience/Work History
        var experiencePatterns = new[]
        {
            @"\b(professional\s+)?experience\b", @"\bwork\s+(history|experience)\b",
            @"\bemployment\s+history\b", @"\bcareer\s+(summary|history)\b",
            @"\bjob\s+history\b"
        };

        if (experiencePatterns.Any(p => Regex.IsMatch(textLower, p)))
        {
            sections.Add("Experience");
        }

        // Education
        var educationPatterns = new[]
        {
            @"\beducation(al)?\s+(background|qualifications?)?\b",
            @"\bacademic\s+(background|qualifications?)\b",
            @"\bdegree\b", @"\buniversity\b", @"\bcollege\b"
        };

        if (educationPatterns.Any(p => Regex.IsMatch(textLower, p)))
        {
            sections.Add("Education");
        }

        // Skills
        var skillsPatterns = new[]
        {
            @"\b(technical\s+)?skills\b", @"\bcore\s+competencies\b",
            @"\bproficiencies\b", @"\bexpertise\b"
        };

        if (skillsPatterns.Any(p => Regex.IsMatch(textLower, p)))
        {
            sections.Add("Skills");
        }

        // Professional Summary/Objective
        var summaryPatterns = new[]
        {
            @"\bprofessional\s+summary\b", @"\bcareer\s+objective\b",
            @"\bprofile\s+summary\b", @"\babout\s+me\b"
        };

        if (summaryPatterns.Any(p => Regex.IsMatch(textLower, p)))
        {
            sections.Add("Summary");
        }

        return sections;
    }

    private bool CheckExperienceIndicators(string text)
    {
        var textLower = text.ToLower();

        // Look for work experience patterns
        var experienceIndicators = new[]
        {
            @"\b(19|20)\d{2}\s*[-–]\s*(present|current|(19|20)\d{2})\b",  // Date ranges
            @"\b\d+\+?\s*years?\s+(of\s+)?(experience|exp)\b",  // "5 years of experience"
            @"\b(worked|working)\s+(at|for|with)\b",
            @"\b(position|role|designation)\s*:\s*\w+",
            @"\b(responsibilities|duties)\s*:\b",
            @"\b(managed|led|developed|implemented|coordinated)\b.*\bteam\b"
        };

        return experienceIndicators.Count(p => Regex.IsMatch(textLower, p)) >= 2;
    }

    private bool CheckEducationIndicators(string text)
    {
        var textLower = text.ToLower();

        var educationIndicators = new[]
        {
            @"\b(bachelor|master|phd|doctorate|diploma|mba|btech|mtech|bsc|msc|ba|ma)\b",
            @"\b(graduated|graduation)\b",
            @"\b(cgpa|gpa|percentage)\s*:?\s*\d",
            @"\buniversity\s+of\b",
            @"\b(college|institute|school)\s+of\b"
        };

        return educationIndicators.Count(p => Regex.IsMatch(textLower, p)) >= 2;
    }

    private bool CheckSkillsIndicators(string text, List<string> keyPhrases)
    {
        var technicalSkills = new[]
        {
            "python", "java", "javascript", "c++", "sql", "react", "angular",
            "machine learning", "data analysis", "project management", "leadership",
            "communication", "teamwork", "problem solving", "aws", "azure", "docker"
        };

        var matchedSkills = keyPhrases.Count(kp => 
            technicalSkills.Any(skill => kp.Contains(skill, StringComparison.OrdinalIgnoreCase))
        );

        return matchedSkills >= 2 || text.ToLower().Contains("skills");
    }

    private int AnalyzeResumeStructure(string text)
    {
        int score = 0;

        // Check for bullet points (common in resumes)
        if (Regex.Matches(text, @"^\s*[•·▪▸►]\s*", RegexOptions.Multiline).Count >= 3)
            score += 5;

        // Check for section headers (usually all caps or title case)
        if (Regex.Matches(text, @"^[A-Z\s]{3,}$", RegexOptions.Multiline).Count >= 2)
            score += 5;

        // Check for proper formatting (not just a wall of text)
        var lines = text.Split('\n');
        if (lines.Length > 10 && lines.Count(l => string.IsNullOrWhiteSpace(l)) > 3)
            score += 5;

        return score;
    }

    private int CheckCareerKeywords(List<string> keyPhrases)
    {
        var careerKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "experience", "responsibilities", "achievements", "projects",
            "certification", "training", "career", "professional",
            "employment", "position", "management", "leadership",
            "developed", "implemented", "coordinated", "managed"
        };

        return keyPhrases.Count(kp => 
            careerKeywords.Any(keyword => kp.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        );
    }

    private string GenerateDetailedFeedback(
        List<string> sections, 
        bool hasExperience, 
        bool hasEducation, 
        bool hasSkills,
        int careerKeywords)
    {
        var feedback = new List<string>();

        if (sections.Count < 2)
            feedback.Add("Missing required resume sections (need at least 2: Experience, Education, Skills, or Summary).");
        
        if (!hasExperience)
            feedback.Add("No work experience information found.");
        
        if (!hasEducation)
            feedback.Add("No education information found.");
        
        if (!hasSkills)
            feedback.Add("No skills section found.");

        if (careerKeywords < 3)
            feedback.Add("Insufficient career-related content.");

        return feedback.Any() ? string.Join(" ", feedback) : "Document structure doesn't match resume format.";
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public double Confidence { get; set; }
    public string Message { get; set; }
    public string DocumentType { get; set; }
    public List<string> MatchedSections { get; set; } = new();
    public string Reason { get; set; }
}