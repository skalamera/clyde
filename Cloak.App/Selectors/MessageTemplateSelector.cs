using System.Windows;
using System.Windows.Controls;
using Cloak.App.Models;

namespace Cloak.App.Selectors
{
    public class MessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? MicrophoneTemplate { get; set; }
        public DataTemplate? SystemTemplate { get; set; }
        public DataTemplate? SuggestionTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is ConversationMessage message)
            {
                return message.Type switch
                {
                    MessageType.Microphone => MicrophoneTemplate,
                    MessageType.System => SystemTemplate,
                    MessageType.Suggestion => SuggestionTemplate,
                    _ => base.SelectTemplate(item, container)
                };
            }

            return base.SelectTemplate(item, container);
        }
    }
}
