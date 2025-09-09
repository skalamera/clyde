using System;
using System.Collections.Generic;

namespace Cloak.App.Models
{
    public enum MessageType
    {
        Microphone,
        System,
        Suggestion
    }

    public class ConversationMessage
    {
        public string Content { get; set; } = string.Empty;
        public MessageType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool CanForceSuggestion { get; set; } = false;
        
        public string DisplayName => Type switch
        {
            MessageType.Microphone => "🎤 You",
            MessageType.System => "🔊 Remote", 
            MessageType.Suggestion => "✨ AI Suggestion",
            _ => "Unknown"
        };
    }

    public class AiSuggestion
    {
        public string Question { get; set; } = string.Empty;
        public List<string> TalkingPoints { get; set; } = new();
        public string ConversationalAnswer { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string OriginalTranscript { get; set; } = string.Empty;
    }
}
