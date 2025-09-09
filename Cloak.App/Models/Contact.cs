using System;
using System.Collections.Generic;

namespace Cloak.App.Models
{
    public class Contact
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<MeetingNote> MeetingNotes { get; set; } = new();
    }

    public class MeetingNote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime MeetingDate { get; set; }
        public string Transcript { get; set; } = string.Empty;
        public string GeneratedNotes { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> KeyPoints { get; set; } = new();
        public List<string> ActionItems { get; set; } = new();
    }
}
