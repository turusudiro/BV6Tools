using Microsoft.Xaml.Behaviors;
using Wpf.Ui.Controls;

namespace BV6Tools.Behaviors
{
    public class NumberBoxEmptyBehavior : Behavior<NumberBox>
    {
        public static readonly DependencyProperty NullableValueProperty = DependencyProperty.Register(
            nameof(NullableValue),
            typeof(bool),
            typeof(NumberBoxEmptyBehavior));

        public bool NullableValue
        {
            get => (bool)GetValue(NullableValueProperty);
            set => SetValue(NullableValueProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.TextChanged += AssociatedObject_TextChanged;
            AssociatedObject.ValueChanged += AssociatedObject_ValueChanged;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.TextChanged -= AssociatedObject_TextChanged;
            AssociatedObject.ValueChanged -= AssociatedObject_ValueChanged;
        }

        bool self;

        private void AssociatedObject_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (double.TryParse(AssociatedObject.Text, out double value))
            {
                UpdateValue(value);
            }
            else
            {
                if (NullableValue)
                {
                    UpdateValue(null);
                }
                else
                {
                    UpdateValue(AssociatedObject.Minimum);
                }
                AssociatedObject.Text = string.Empty;
            }
        }

        private void UpdateValue(double? value)
        {
            self = true;
            AssociatedObject.Value = value;
            self = false;
        }

        private void AssociatedObject_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (self) return;
            if (string.IsNullOrEmpty(AssociatedObject.Text))
            {
                AssociatedObject.Value = AssociatedObject.Minimum;
            }
        }
    }
}