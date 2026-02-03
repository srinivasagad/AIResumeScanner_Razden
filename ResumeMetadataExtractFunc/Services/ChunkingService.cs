namespace ResumeMetadataExtractFunc.Services
{
    public class ChunkingService
    {
        private readonly int _chunkSize;
        private readonly int _chunkOverlap;

        public ChunkingService(int chunkSize = 500, int chunkOverlap = 50)
        {
            _chunkSize = chunkSize;
            _chunkOverlap = chunkOverlap;
        }

        public List<string> ChunkText(string text)
        {
            var chunks = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var currentChunk = new List<string>();
            int currentLength = 0;

            foreach (var word in words)
            {
                if (currentLength + word.Length > _chunkSize && currentChunk.Count > 0)
                {
                    chunks.Add(string.Join(" ", currentChunk));

                    // Keep overlap words
                    int overlapWords = Math.Min(_chunkOverlap / 10, currentChunk.Count);
                    currentChunk = currentChunk.Skip(currentChunk.Count - overlapWords).ToList();
                    currentLength = currentChunk.Sum(w => w.Length + 1);
                }

                currentChunk.Add(word);
                currentLength += word.Length + 1;
            }

            if (currentChunk.Count > 0)
            {
                chunks.Add(string.Join(" ", currentChunk));
            }

            return chunks;
        }

        // Alternative: Chunk by sentences
        public List<string> ChunkBySentences(string text, int sentencesPerChunk = 5)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToList();

            var chunks = new List<string>();
            for (int i = 0; i < sentences.Count; i += sentencesPerChunk)
            {
                var chunk = string.Join(". ", sentences.Skip(i).Take(sentencesPerChunk)) + ".";
                chunks.Add(chunk);
            }

            return chunks;
        }
    }
}
