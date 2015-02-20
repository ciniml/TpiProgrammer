using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interactivity;
using Livet.Behaviors.Messaging;
using Livet.Messaging;

namespace TpiProgrammer.View
{
    public class ShowMessageBoxAction : InteractionMessageAction<FrameworkElement>
    {
        public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
            "Message", typeof (string), typeof (ShowMessageBoxAction), new PropertyMetadata(default(string)));

        public string Message
        {
            get { return (string) GetValue(MessageProperty); }
            set { SetValue(MessageProperty, value); }
        }

        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            "Caption", typeof (string), typeof (ShowMessageBoxAction), new PropertyMetadata(default(string)));

        public string Caption
        {
            get { return (string) GetValue(CaptionProperty); }
            set { SetValue(CaptionProperty, value); }
        }

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            "Icon", typeof (MessageBoxImage), typeof (ShowMessageBoxAction), new PropertyMetadata(default(MessageBoxImage)));

        public MessageBoxImage Icon
        {
            get { return (MessageBoxImage) GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
            "Owner", typeof (Window), typeof (ShowMessageBoxAction), new PropertyMetadata(default(Window)));

        public Window Owner
        {
            get { return (Window) GetValue(OwnerProperty); }
            set { SetValue(OwnerProperty, value); }
        }

        protected override void InvokeAction(InteractionMessage message)
        {
            var targetMessage = (MessageBoxMessage)message;
            var formattedMessage = String.Format(this.Message, targetMessage.Parameters);
            targetMessage.Response = MessageBox.Show(this.Owner, formattedMessage, this.Caption, targetMessage.Button, this.Icon);
        }
    }

    public class MessageBoxMessage : InteractionMessage
    {
        public MessageBoxButton Button { get; private set; }
        public object[] Parameters { get; private set; }
        public MessageBoxResult Response { get; set; }

        public MessageBoxMessage(string messageKey, MessageBoxButton button, params object[] parameters) : base(messageKey)
        {
            this.Button = button;
            this.Parameters = parameters;
            this.Response = MessageBoxResult.OK;
        }
        public MessageBoxMessage(string messageKey) : this(messageKey, MessageBoxButton.OK)
        {
        }
    }
}
