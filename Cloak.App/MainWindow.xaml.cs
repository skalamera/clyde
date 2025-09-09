using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Cloak.Services.Audio;
using Cloak.Services.Transcription;
using Cloak.Services.Assistant;
using Cloak.App.Models;
using Cloak.App.Services;

namespace Cloak.App
{
    public partial class MainWindow : Window, IDisposable
    {
        private IAudioCaptureService? _micCaptureService;
        private IAudioCaptureService? _systemCaptureService;
        private readonly ITranscriptionService _transcriptionService;
        private readonly IAssistantService _assistantService;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _disposed = false;
        private readonly ObservableCollection<ConversationMessage> _conversationMessages = new();
        private readonly ObservableCollection<AiSuggestion> _aiSuggestions = new();
        private ConversationMessage? _lastForcedMessage = null;
        private readonly IContactService _contactService;
        private int _currentSuggestionIndex = 0;
        private readonly ObservableCollection<Contact> _contacts = new();
        private Contact? _selectedContact = null;
        private bool _isUserNavigating = false;
        private DateTime _lastUserInteraction = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize conversation view and contacts
            ConversationItems.ItemsSource = _conversationMessages;
            ContactsList.ItemsSource = _contacts;
            
            // Add scroll event handler to detect user interaction
            SuggestionsScroll.ScrollChanged += OnSuggestionsScrollChanged;
            
            // Enable console output for debugging
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
            }

            _micCaptureService = null;
            _systemCaptureService = null;
            
            // Load Azure Speech configuration from environment variables
            var azureKey = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var azureRegion = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
            System.Diagnostics.Debug.WriteLine($"Azure Key found: {!string.IsNullOrWhiteSpace(azureKey)}");
            System.Diagnostics.Debug.WriteLine($"Azure Region found: {!string.IsNullOrWhiteSpace(azureRegion)}");
            if (!string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureRegion))
            {
                _transcriptionService = new AzureSpeechTranscriptionService(azureKey!, azureRegion!);
                System.Diagnostics.Debug.WriteLine("✅ Using Azure Speech Service for transcription");
            }
            else
            {
                // Fallback to placeholder service if no valid keys found
                _transcriptionService = new PlaceholderTranscriptionService();
                System.Diagnostics.Debug.WriteLine("❌ No Azure Speech keys found, using placeholder transcription service");
            }
            // Load LLM configuration from environment variables - try OpenAI first, then Gemini, then placeholder
            var openAiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            System.Diagnostics.Debug.WriteLine($"OpenAI Key found: {!string.IsNullOrWhiteSpace(openAiKey)}");
            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                var llm = new Cloak.Services.LLM.OpenAiLlmClient(openAiKey!);
                _assistantService = new LlmAssistantService(llm);
                _contactService = new ContactService(llm);
                System.Diagnostics.Debug.WriteLine("✅ Using OpenAI for assistant service");
            }
            else
            {
                var geminiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                System.Diagnostics.Debug.WriteLine($"Gemini Key found: {!string.IsNullOrWhiteSpace(geminiKey)}");
                if (!string.IsNullOrWhiteSpace(geminiKey))
                {
                    var llm = new Cloak.Services.LLM.GeminiLlmClient(geminiKey!);
                    _assistantService = new LlmAssistantService(llm);
                    _contactService = new ContactService(llm);
                    System.Diagnostics.Debug.WriteLine("✅ Using Gemini for assistant service");
                }
                else
                {
                    // Fallback to placeholder if no valid keys found
                    _assistantService = new PlaceholderAssistantService();
                    _contactService = new ContactService(new Cloak.Services.LLM.PlaceholderLlmClient());
                    System.Diagnostics.Debug.WriteLine("❌ No LLM keys found, using placeholder assistant service");
                }
            }

            _transcriptionService.MicTranscriptReceived += OnMicTranscriptReceived;
            _transcriptionService.SystemTranscriptReceived += OnSystemTranscriptReceived;
            _assistantService.SuggestionReceived += OnSuggestionReceived;

            // Audio level monitoring removed for cleaner UI
        }

        private async void OnStartClick(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            
            // Update status indicator
            StatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0xA7, 0x45)); // Green
            StatusText.Text = "Recording active";

            // Select capture source
            var mode = (CaptureMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            if (mode == "System")
            {
                _systemCaptureService = new WasapiLoopbackCaptureService();
                await _systemCaptureService.StartAsync(sample => _transcriptionService.PushSystemAudio(sample));
            }
            else if (mode == "Both")
            {
                _micCaptureService = new WasapiMicCaptureService();
                _systemCaptureService = new WasapiLoopbackCaptureService();
                await _micCaptureService.StartAsync(sample => _transcriptionService.PushMicAudio(sample));
                await _systemCaptureService.StartAsync(sample => _transcriptionService.PushSystemAudio(sample));
            }
            else
            {
                _micCaptureService = new WasapiMicCaptureService();
                await _micCaptureService.StartAsync(sample => _transcriptionService.PushMicAudio(sample));
            }
        }

        private async void OnStopClick(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            
            // Update status indicator
            StatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x35, 0x45)); // Red
            StatusText.Text = "Stopping...";
            
            await StopServicesAsync();
            
            // Generate meeting notes if there's conversation content
            await GenerateMeetingNotesAsync();
            
            // Final status update
            StatusText.Text = "Ready to start";
            StartButton.IsEnabled = true;
        }

        private async Task StopServicesAsync()
        {
            try
            {
                // Stop with timeout to prevent hanging
                var stopTasks = new List<Task>();
                
                if (_micCaptureService != null)
                {
                    stopTasks.Add(Task.Run(async () =>
                    {
                        try { await _micCaptureService.StopAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Mic stop error: {ex}"); }
                        finally { _micCaptureService = null; }
                    }));
                }
                
                if (_systemCaptureService != null)
                {
                    stopTasks.Add(Task.Run(async () =>
                    {
                        try { await _systemCaptureService.StopAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"System stop error: {ex}"); }
                        finally { _systemCaptureService = null; }
                    }));
                }

                // Wait for all stop operations with timeout
                if (stopTasks.Count > 0)
                {
                    await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromSeconds(3));
                }
            }
            catch (TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine("Stop operation timed out");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stop error: {ex}");
            }
            finally
            {
                try { _transcriptionService.Flush(); }
                catch { }
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            await StopServicesAsync();
            await DisposeAsync();
            base.OnClosed(e);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                
                // Properly dispose async services
                if (_transcriptionService is IAsyncDisposable asyncDisposableTranscription)
                    await asyncDisposableTranscription.DisposeAsync();
                else if (_transcriptionService is IDisposable disposableTranscription)
                    disposableTranscription.Dispose();
                    
                if (_assistantService is IAsyncDisposable asyncDisposableAssistant)
                    await asyncDisposableAssistant.DisposeAsync();
                else if (_assistantService is IDisposable disposableAssistant)
                    disposableAssistant.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DisposeAsync error: {ex}");
            }
        }

        public void Dispose()
        {
            // Synchronous dispose that blocks on async disposal
            try
            {
                DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex}");
            }
        }

        ~MainWindow()
        {
            Dispose();
        }

        private void OnMicTranscriptReceived(object? sender, string text)
        {
            if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_disposed)
                    {
                        _conversationMessages.Add(new ConversationMessage
                        {
                            Content = text,
                            Type = MessageType.Microphone,
                            Timestamp = DateTime.Now
                        });
                        _assistantService.ProcessContext(text);
                        ScrollToBottom();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mic transcript error: {ex}");
            }
        }

        private void OnSystemTranscriptReceived(object? sender, string text)
        {
            if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_disposed)
                    {
                        _conversationMessages.Add(new ConversationMessage
                        {
                            Content = text,
                            Type = MessageType.System,
                            Timestamp = DateTime.Now,
                            CanForceSuggestion = true
                        });
                        _assistantService.ProcessContext(text);
                        ScrollToBottom();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"System transcript error: {ex}");
            }
        }

        private void OnSuggestionReceived(object? sender, string suggestion)
        {
            if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested) return;
            try
            {
                System.Diagnostics.Debug.WriteLine($"🤖 Suggestion received: {suggestion}");
                
                // Parse the suggestion into structured format
                var aiSuggestion = ParseSuggestion(suggestion);
                
                Dispatcher.Invoke(() =>
                {
                    if (!_disposed)
                    {
                        _aiSuggestions.Add(aiSuggestion); // Add to collection
                        
                        // Only auto-switch to newest if user hasn't been navigating recently (within 30 seconds)
                        var timeSinceLastInteraction = DateTime.Now - _lastUserInteraction;
                        if (!_isUserNavigating || timeSinceLastInteraction.TotalSeconds > 30)
                        {
                            _currentSuggestionIndex = _aiSuggestions.Count - 1; // Show newest suggestion
                            _isUserNavigating = false; // Reset navigation flag
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"🤖 Suggestion added to UI. Total suggestions: {_aiSuggestions.Count}. Auto-switched: {!_isUserNavigating || timeSinceLastInteraction.TotalSeconds > 30}");
                        UpdateCarouselDisplay();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion error: {ex}");
            }
        }

        private AiSuggestion ParseSuggestion(string suggestion)
        {
            // Use the forced message if available, otherwise get the most recent remote message
            var questionMessage = _lastForcedMessage ?? _conversationMessages
                .Where(m => m.Type == MessageType.System)
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();
            
            // Clear the forced message after using it
            _lastForcedMessage = null;

            var lines = suggestion.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var aiSuggestion = new AiSuggestion
            {
                Timestamp = DateTime.Now,
                OriginalTranscript = suggestion
            };

            // Use the actual interviewer's question if available
            if (questionMessage != null && !string.IsNullOrWhiteSpace(questionMessage.Content))
            {
                aiSuggestion.Question = questionMessage.Content.Trim();
            }
            else
            {
                // Fallback: try to extract question from suggestion text
                var questionLine = lines.FirstOrDefault(l => l.Contains('?') || l.ToLower().Contains("question"));
                aiSuggestion.Question = questionLine?.Trim() ?? "How would you handle this situation?";
            }

            // Extract talking points (look for bullet points or numbered items)
            var talkingPoints = lines.Where(l => 
                l.Trim().StartsWith("•") || 
                l.Trim().StartsWith("-") || 
                l.Trim().StartsWith("*") ||
                System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^\d+\.")).ToList();
            
            if (talkingPoints.Any())
            {
                aiSuggestion.TalkingPoints = talkingPoints.Select(tp => 
                    tp.Trim().TrimStart('•', '-', '*').Trim()
                      .TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.').Trim()).ToList();
                
                // Remove talking points from the main suggestion text to avoid duplication
                var suggestionWithoutBullets = string.Join("\n", lines.Where(l => 
                    !l.Trim().StartsWith("•") && 
                    !l.Trim().StartsWith("-") && 
                    !l.Trim().StartsWith("*") &&
                    !System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^\d+\.")));
                
                aiSuggestion.ConversationalAnswer = suggestionWithoutBullets
                    .Replace("Here's a suggestion:", "")
                    .Replace("Suggestion:", "")
                    .Replace("Paragraph:", "")
                    .Replace("Answer:", "")
                    .Trim();
            }
            else
            {
                // Generate contextual talking points based on the question
                aiSuggestion.TalkingPoints = new List<string>
                {
                    "Break down the key components of the question",
                    "Share relevant experience and examples", 
                    "Explain your thought process and approach",
                    "Discuss potential challenges and solutions",
                    "Highlight the impact and outcomes"
                };
                
                // Use the full suggestion as the conversational answer, cleaned up
                var cleanedSuggestion = suggestion.Replace("Here's a suggestion:", "").Replace("Suggestion:", "").Replace("Paragraph:", "").Replace("Answer:", "").Trim();
                aiSuggestion.ConversationalAnswer = cleanedSuggestion.Length > 50 ? 
                    cleanedSuggestion : 
                    "I would approach this by first understanding the core requirements, then developing a structured solution that addresses the key concerns while maintaining flexibility for future needs. Let me walk you through my thinking...";
            }

            return aiSuggestion;
        }

        private void OnSuggestNowClick(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;
            System.Diagnostics.Debug.WriteLine("🔥 Get Suggestion button clicked - calling ForceSuggest()");
            _assistantService.ForceSuggest();
        }

        private void OnForceSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;
            
            try
            {
                var button = sender as System.Windows.Controls.Button;
                var message = button?.Tag as ConversationMessage;
                
                if (message != null)
                {
                    System.Diagnostics.Debug.WriteLine($"🚀 Force suggestion for: {message.Content}");
                    
                    // Store the specific message that triggered this suggestion
                    _lastForcedMessage = message;
                    
                    _assistantService.ProcessContext(message.Content);
                    _assistantService.ForceSuggest();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Force suggestion error: {ex}");
            }
        }

        private void ScrollToBottom()
        {
            try
            {
                ConversationScroll?.ScrollToBottom();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scroll error: {ex}");
            }
        }

        private void ScrollSuggestionsToTop()
        {
            try
            {
                SuggestionsScroll?.ScrollToTop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestions scroll error: {ex}");
            }
        }

        private async Task GenerateMeetingNotesAsync()
        {
            try
            {
                // Get only the conversation messages (exclude AI suggestions)
                var conversationOnly = _conversationMessages.Where(m => 
                    m.Type == MessageType.Microphone || m.Type == MessageType.System).ToList();
                
                if (!conversationOnly.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No conversation to generate notes for");
                    return;
                }

                // Create transcript from conversation
                var transcript = string.Join("\n", conversationOnly.Select(m => 
                    $"[{m.Timestamp:HH:mm:ss}] {m.DisplayName}: {m.Content}"));

                System.Diagnostics.Debug.WriteLine("Generating meeting notes...");
                StatusText.Text = "Generating meeting notes...";

                // Generate meeting notes
                var meetingNotes = await _contactService.GenerateMeetingNotesAsync(transcript);

                // Show contact save dialog
                await ShowContactSaveDialogAsync(transcript, meetingNotes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating meeting notes: {ex}");
                System.Windows.MessageBox.Show("Error generating meeting notes. Please try again.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private async Task ShowContactSaveDialogAsync(string transcript, string meetingNotes)
        {
            var result = System.Windows.MessageBox.Show(
                "Would you like to save this conversation and meeting notes as a contact?", 
                "Save Contact", 
                System.Windows.MessageBoxButton.YesNo, 
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // For now, create a simple contact with the meeting notes
                // In a real app, you'd show a proper dialog to enter contact details
                var contact = new Contact
                {
                    Name = "Remote Participant",
                    Company = "Interview Session",
                    Role = "Interviewee/Interviewer",
                    CreatedAt = DateTime.Now
                };

                var meetingNote = new MeetingNote
                {
                    MeetingDate = DateTime.Now,
                    Transcript = transcript,
                    GeneratedNotes = meetingNotes,
                    Summary = "Interview/Meeting Session"
                };

                contact.MeetingNotes.Add(meetingNote);
                await _contactService.SaveContactAsync(contact);

                System.Windows.MessageBox.Show(
                    $"Contact and meeting notes saved successfully!\n\nMeeting Notes:\n{meetingNotes}", 
                    "Success", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Information);
            }
        }

        private void OnPrevSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (_currentSuggestionIndex > 0)
            {
                _isUserNavigating = true;
                _lastUserInteraction = DateTime.Now;
                _currentSuggestionIndex--;
                UpdateCarouselDisplay();
            }
        }

        private void OnNextSuggestionClick(object sender, RoutedEventArgs e)
        {
            if (_currentSuggestionIndex < _aiSuggestions.Count - 1)
            {
                _isUserNavigating = true;
                _lastUserInteraction = DateTime.Now;
                _currentSuggestionIndex++;
                UpdateCarouselDisplay();
            }
        }

        private void UpdateCarouselDisplay()
        {
            try
            {
                if (_aiSuggestions.Count == 0)
                {
                    // Show empty state
                    EmptyState.Visibility = System.Windows.Visibility.Visible;
                    CurrentSuggestionCard.Visibility = System.Windows.Visibility.Collapsed;
                    SuggestionCounter.Text = "No suggestions yet";
                    PrevButton.IsEnabled = false;
                    NextButton.IsEnabled = false;
                    return;
                }

                // Hide empty state and show current suggestion
                EmptyState.Visibility = System.Windows.Visibility.Collapsed;
                CurrentSuggestionCard.Visibility = System.Windows.Visibility.Visible;

                // Update counter with new suggestion indicator
                var counterText = $"Suggestion {_currentSuggestionIndex + 1} of {_aiSuggestions.Count}";
                if (_isUserNavigating && _currentSuggestionIndex < _aiSuggestions.Count - 1)
                {
                    counterText += " 🔔 NEW";
                }
                SuggestionCounter.Text = counterText;

                // Update navigation buttons
                PrevButton.IsEnabled = _currentSuggestionIndex > 0;
                NextButton.IsEnabled = _currentSuggestionIndex < _aiSuggestions.Count - 1;

                // Update current suggestion content
                var currentSuggestion = _aiSuggestions[_currentSuggestionIndex];
                CurrentQuestion.Text = currentSuggestion.Question;
                CurrentTalkingPoints.ItemsSource = currentSuggestion.TalkingPoints;
                CurrentAnswer.Text = currentSuggestion.ConversationalAnswer;
                CurrentTimestamp.Text = currentSuggestion.Timestamp.ToString("HH:mm:ss");

                // Scroll to top of the current suggestion
                SuggestionsScroll?.ScrollToTop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating carousel display: {ex}");
            }
        }

        private void OnSuggestionsScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // User is actively scrolling, so they're reading - don't auto-switch
            if (e.VerticalChange != 0)
            {
                _isUserNavigating = true;
                _lastUserInteraction = DateTime.Now;
            }
        }

        private void OnSuggestionsTabClick(object sender, RoutedEventArgs e)
        {
            // Show AI Suggestions panel
            SuggestionsPanel.Visibility = System.Windows.Visibility.Visible;
            ContactsPanel.Visibility = System.Windows.Visibility.Collapsed;
            
            // Update tab button styles
            SuggestionsTab.Style = (Style)FindResource("FuturisticButton");
            ContactsTab.Style = (Style)FindResource("SecondaryButton");
            
            // Show/hide suggest now button
            SuggestNowBtn.Visibility = System.Windows.Visibility.Visible;
        }

        private async void OnContactsTabClick(object sender, RoutedEventArgs e)
        {
            // Show Contacts panel
            SuggestionsPanel.Visibility = System.Windows.Visibility.Collapsed;
            ContactsPanel.Visibility = System.Windows.Visibility.Visible;
            
            // Update tab button styles
            SuggestionsTab.Style = (Style)FindResource("SecondaryButton");
            ContactsTab.Style = (Style)FindResource("FuturisticButton");
            
            // Hide suggest now button
            SuggestNowBtn.Visibility = System.Windows.Visibility.Collapsed;
            
            // Load contacts
            await LoadContactsAsync();
        }

        private async Task LoadContactsAsync()
        {
            try
            {
                var contacts = await _contactService.GetAllContactsAsync();
                _contacts.Clear();
                foreach (var contact in contacts)
                {
                    _contacts.Add(contact);
                }

                // Update empty state visibility
                ContactsEmptyState.Visibility = _contacts.Count == 0 ? 
                    System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading contacts: {ex}");
                System.Windows.MessageBox.Show("Error loading contacts. Please try again.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private async void OnRefreshContactsClick(object sender, RoutedEventArgs e)
        {
            await LoadContactsAsync();
        }

        private void OnContactClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement element && element.DataContext is Contact contact)
            {
                OnContactSelected(contact);
            }
        }

        private void OnContactSelected(Contact contact)
        {
            try
            {
                _selectedContact = contact;
                
                // Update contact header
                ContactName.Text = contact.Name;
                ContactCompany.Text = contact.Company;
                ContactRole.Text = contact.Role;
                ContactHeader.Visibility = System.Windows.Visibility.Visible;
                
                // Update meeting notes
                MeetingNotesList.ItemsSource = contact.MeetingNotes;
                NotesScroll.Visibility = System.Windows.Visibility.Visible;
                ContactsEmptyState.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error selecting contact: {ex}");
            }
        }

        private void OnCopyNotesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button && button.Tag is MeetingNote meetingNote)
                {
                    System.Windows.Clipboard.SetText(meetingNote.GeneratedNotes);
                    System.Windows.MessageBox.Show("Meeting notes copied to clipboard!", 
                        "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying notes: {ex}");
                System.Windows.MessageBox.Show("Error copying notes to clipboard.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void OnExportNotesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button && button.Tag is MeetingNote meetingNote)
                {
                    var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                        DefaultExt = "txt",
                        FileName = $"Meeting_Notes_{meetingNote.MeetingDate:yyyy-MM-dd_HH-mm}.txt"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        var content = $"Meeting Notes - {meetingNote.MeetingDate:MMM dd, yyyy HH:mm}\n";
                        content += $"Contact: {_selectedContact?.Name ?? "Unknown"}\n";
                        content += $"Company: {_selectedContact?.Company ?? "Unknown"}\n";
                        content += new string('=', 50) + "\n\n";
                        content += meetingNote.GeneratedNotes;
                        content += "\n\n" + new string('-', 50) + "\n";
                        content += "Original Transcript:\n\n";
                        content += meetingNote.Transcript;

                        System.IO.File.WriteAllText(saveFileDialog.FileName, content);
                        System.Windows.MessageBox.Show($"Meeting notes exported to:\n{saveFileDialog.FileName}", 
                            "Export Successful", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting notes: {ex}");
                System.Windows.MessageBox.Show("Error exporting notes to file.", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}

