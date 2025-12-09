namespace ResumeParserWebApi.Models
{
    public class FileSentimentResult
    {
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string FileName { get; set; }

        public string FileUrl { get; set; }
        public AISentiment AISentiment { get; set; }
    }

    public class ConfidenceScores
    {
        public double? Positive { get; set; }
        public double? Negative { get; set; }
        public double? Neutral { get; set; }
    }

    public class RequirementMatch
    {
        public string? Requirement { get; set; }
        public bool? IsMatched { get; set; }
        public string? Evidence { get; set; }
    }


    public class AISentiment
    {
        public string? OverallSentiment { get; set; }
        public ConfidenceScores? ConfidenceScores { get; set; }
        public string? MatchWithJobDescription { get; set; }
        public bool? IsTailored { get; set; }
        public string? Reasoning { get; set; }
        public List<RequirementMatch>? Requirements { get; set; }
    }

    public class ApiResponse
    {
        public string JobDescription { get; set; }
        public string Query { get; set; }
        public string AISearchServiceQuery { get; set; }
        public int ResumesRetrievedCount { get; set; }
        public List<FileSentimentResult> Results { get; set; }
    }

    public class MatchProfileRequest
    {
        public string UserQuery { get; set; }
        public string JobDescription { get; set; }
    }
}
