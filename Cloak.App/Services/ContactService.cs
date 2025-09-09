using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cloak.App.Models;

namespace Cloak.App.Services
{
    public interface IContactService
    {
        Task<List<Contact>> GetAllContactsAsync();
        Task<Contact?> GetContactByIdAsync(string id);
        Task<Contact> SaveContactAsync(Contact contact);
        Task<MeetingNote> AddMeetingNoteAsync(string contactId, MeetingNote meetingNote);
        Task<string> GenerateMeetingNotesAsync(string transcript);
    }

    public class ContactService : IContactService
    {
        private readonly string _dataPath;
        private readonly Cloak.Services.LLM.ILlmClient _llmClient;

        public ContactService(Cloak.Services.LLM.ILlmClient llmClient)
        {
            _llmClient = llmClient;
            _dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clyde", "contacts.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        }

        public async Task<List<Contact>> GetAllContactsAsync()
        {
            try
            {
                if (!File.Exists(_dataPath))
                    return new List<Contact>();

                var json = await File.ReadAllTextAsync(_dataPath);
                return JsonSerializer.Deserialize<List<Contact>>(json) ?? new List<Contact>();
            }
            catch
            {
                return new List<Contact>();
            }
        }

        public async Task<Contact?> GetContactByIdAsync(string id)
        {
            var contacts = await GetAllContactsAsync();
            return contacts.FirstOrDefault(c => c.Id == id);
        }

        public async Task<Contact> SaveContactAsync(Contact contact)
        {
            var contacts = await GetAllContactsAsync();
            var existingIndex = contacts.FindIndex(c => c.Id == contact.Id);
            
            if (existingIndex >= 0)
            {
                contacts[existingIndex] = contact;
            }
            else
            {
                contacts.Add(contact);
            }

            var json = JsonSerializer.Serialize(contacts, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_dataPath, json);
            
            return contact;
        }

        public async Task<MeetingNote> AddMeetingNoteAsync(string contactId, MeetingNote meetingNote)
        {
            var contact = await GetContactByIdAsync(contactId);
            if (contact == null)
                throw new ArgumentException("Contact not found", nameof(contactId));

            contact.MeetingNotes.Add(meetingNote);
            await SaveContactAsync(contact);
            
            return meetingNote;
        }

        public async Task<string> GenerateMeetingNotesAsync(string transcript)
        {
            try
            {
                var prompt = $@"Please analyze this interview/meeting transcript and generate comprehensive meeting notes in the following format:

## Meeting Summary
[Provide a brief 2-3 sentence summary of the meeting]

## Key Discussion Points
- [List the main topics discussed]
- [Include important details and context]

## Action Items
- [List any tasks, follow-ups, or next steps mentioned]
- [Include deadlines if mentioned]

## Important Insights
- [Highlight key insights, decisions, or revelations]
- [Note any concerns or challenges discussed]

## Follow-up Questions
- [Suggest relevant follow-up questions for future conversations]

Transcript:
{transcript}

Please provide detailed, professional meeting notes:";

                var response = await _llmClient.GetSuggestionAsync(prompt);
                return response ?? "Unable to generate meeting notes at this time.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating meeting notes: {ex}");
                return "Error generating meeting notes. Please try again.";
            }
        }
    }
}
