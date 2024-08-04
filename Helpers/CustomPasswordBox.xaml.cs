using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Helpers
{
    public sealed partial class CustomPasswordBox : UserControl
    {
        public CustomPasswordBox()
        {
            this.InitializeComponent();
        }


        public static readonly DependencyProperty TooltipProperty =
            DependencyProperty.Register("Tooltip", typeof(string), typeof(CustomPasswordBox), new PropertyMetadata(string.Empty));

        public string Tooltip
        {
            get
            {
                return (string)GetValue(TooltipProperty);
            }
            set
            {
                SetValue(TooltipProperty, value);
            }
        }

        public static readonly DependencyProperty PasswordBoxMarginProperty =
            DependencyProperty.Register("PasswordBoxMargin", typeof(Thickness), typeof(CustomPasswordBox), new PropertyMetadata(new Thickness(0)));

        public Thickness PasswordBoxMargin
        {
            get
            {
                return (Thickness)GetValue(PasswordBoxMarginProperty);
            }
            set
            {
                SetValue(PasswordBoxMarginProperty, value);
            }
        }

        private void RevealButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (RevealButton.IsChecked == true)
            {
                PasswordBox.PasswordRevealMode = PasswordRevealMode.Visible;
                RevealIcon.Glyph = "\uED1A";
            }
            else
            {
                PasswordBox.PasswordRevealMode = PasswordRevealMode.Hidden;
                RevealIcon.Glyph = "\uF78D";
            }
        }

        public string Password
        {
            get => PasswordBox.Password;
            set => PasswordBox.Password = value;
        }
    }
}