using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIResumeScanner_Razden.Models
{
    public class SearchResultModel
    {
        // Ranking
        public int Rank { get; set; }

        // Document Fields
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string FileName { get; set; }
        public string Category { get; set; }
        public List<string> Skills { get; set; } = new List<string>();

        // Chunks
        public List<string> Chunks { get; set; } = new List<string>();
        public int TotalChunks => Chunks?.Count ?? 0;

        // Scores
        public double? SearchScore { get; set; }
        public double? RerankerScore { get; set; }
        public double FinalScore => RerankerScore ?? SearchScore ?? 0;
        public string ScoreLevel => GetScoreLevel(FinalScore);

        // Sentiment Analysis Properties
        public string Sentiment { get; set; }
        public ConfidenceScores ConfidenceScores { get; set; }
        //public double PositiveScore => ConfidenceScores?.Positive ?? 0.0;
        //public double NeutralScore => ConfidenceScores?.Neutral ?? 0.0;
        //public double NegativeScore => ConfidenceScores?.Negative ?? 0.0;
        //public string SentimentLevel => GetSentimentLevel();

        // Job Matching Properties
        public string MatchWithJobDescription { get; set; }
        public bool IsTailored { get; set; }
        public string Reasoning { get; set; }
        public List<Requirement> Requirements { get; set; } = new List<Requirement>();

        // Semantic Search Results
        public List<SemanticCaption> SemanticCaptions { get; set; } = new List<SemanticCaption>();

        // Highlights
        public Dictionary<string, List<string>> Highlights { get; set; } = new Dictionary<string, List<string>>();

        // Metadata
        public string MetadataUrl { get; set; }
        public DateTimeOffset? UploadDate { get; set; }

        // Helper Properties
        public string ContentPreview => GetPreview(Content, 200);
        public List<string> ChunkPreviews => Chunks?.Select(c => GetPreview(c, 200)).ToList() ?? new List<string>();

        // Helper Methods
        private string GetPreview(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Length > maxLength
                ? text.Substring(0, maxLength) + "..."
                : text;
        }

        private string GetScoreLevel(double score)
        {
            return score switch
            {
                >= 3.0 => "🟢 Excellent Match",
                >= 2.0 => "🟡 Good Match",
                >= 1.0 => "🟠 Fair Match",
                _ => "🔴 Weak Match"
            };
        }

        
    }

    // Supporting Classes
    public class ConfidenceScores
    {
        public double Positive { get; set; }
        public double Negative { get; set; }
        public double Neutral { get; set; }
    }

    

    public class SemanticCaption
    {
        public string Text { get; set; }
        public List<SemanticCaptionHighlight> Highlights { get; set; } = new List<SemanticCaptionHighlight>();
    }

    public class SemanticCaptionHighlight
    {
        public string Text { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }
}
