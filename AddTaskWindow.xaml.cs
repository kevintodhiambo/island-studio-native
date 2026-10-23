using System;
using System.Windows;
using System.Windows.Input;

namespace IslandNative
{
    public partial class AddTaskWindow : Window
    {
        readonly Action<string> _onAdd;

        public AddTaskWindow(Action<string> onAdd)
        {
            InitializeComponent();
            _onAdd = onAdd;

            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 150;

            Loaded += (s, e) => { Activate(); Input.Focus(); };
            Deactivated += (s, e) => Close();
            Input.TextChanged += (s, e) => Placeholder.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Collapsed;
            Input.PreviewKeyDown += OnKey;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var t = Input.Text.Trim();
                if (t.Length > 0) { _onAdd(t); Input.Clear(); }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }
    }
}
