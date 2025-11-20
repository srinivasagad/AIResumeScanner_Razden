namespace AIResumeScanner_Razden.Models
{

    public class AiSentiment
    {
        public string? overallSentiment { get; set; }
        public ConfidenceScores confidenceScores { get; set; }
        public string? matchWithJobDescription { get; set; }
        public bool? isTailored { get; set; }
        public string? reasoning { get; set; }
        public List<Requirement> requirements { get; set; }
    }

    

    public class Requirement
    {
        public string? requirement { get; set; }
        public bool? isMatched { get; set; }
        public string? evidence { get; set; }
    }

    public class Result
    {
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? FileName { get; set; }
        public string? FileUrl { get; set; }
        public AiSentiment AISentiment { get; set; }
    }

    public class Root
    {
        public string? jobDescription { get; set; }
        public string? aiSearchServiceQuery { get; set; }
        public int? resumesRetrievedCount { get; set; }
        public List<Result> results { get; set; }
    }



}
