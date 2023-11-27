using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SchedulerDemo;

/// <summary>
/// WPF UpDownControl
/// </summary>
public partial class UpDownControl : UserControl
{
    readonly Regex _numMatch;

    public UpDownControl()
    {
        InitializeComponent();
        _numMatch = new Regex(@"^-?\d+$");
        Maximum = int.MaxValue;
        Minimum = 0;
        tbValue.Text = "0";
        
        // Wait until the size property has value before sampling...
        this.Loaded += (s, e) =>
        {
            Debug.WriteLine($">> Auto-sizing based on font size of {tbValue.FontSize}");

            // 1:1.5 aspect ratio (up arrow)
            imgUp.BeginInit();
            imgUp.Width = tbValue.FontSize / 2;
            imgUp.Height = tbValue.FontSize / 3;
            imgUp.EndInit();

            // 1:1.5 aspect ratio (down arrow)
            imgDown.BeginInit();
            imgDown.Width = tbValue.FontSize / 2;
            imgDown.Height = tbValue.FontSize / 3;
            imgDown.EndInit();
        };
    }

    #region [Control Events]
    void value_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb != null) {
            var text = tb.Text.Insert(tb.CaretIndex, e.Text);
            e.Handled = !_numMatch.IsMatch(text);
        }
    }

    void value_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        if (!_numMatch.IsMatch(tb.Text)) { ResetText(tb); }
        Value = Convert.ToInt32(tb.Text);
        if (Value < Minimum) { Value = Minimum; }
        if (Value > Maximum) { Value = Maximum; }
        RaiseEvent(new RoutedEventArgs(ValueChangedEvent));
    }

    /// <summary>
    /// Check for Up and Down events and update the value accordingly.
    /// </summary>
    private void value_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsDown && e.Key == Key.Up && Value < Maximum) {
            Value += Change;
            RaiseEvent(new RoutedEventArgs(IncreaseClickedEvent));
        }
        else if (e.IsDown && e.Key == Key.Down && Value > Minimum) {
            Value -= Change;
            RaiseEvent(new RoutedEventArgs(DecreaseClickedEvent));
        }
    }

    async void Increase_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        while (e.LeftButton == MouseButtonState.Pressed) {
            if (Value < (Maximum + 1)) {
                await Task.Delay(Change * 8); // smaller amounts should repeat faster
                Value += Change;
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(IncreaseClickedEvent));
            }
        }
    }

    async void Decrease_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        while (e.LeftButton == MouseButtonState.Pressed) {
            if (Value > Minimum) {
                await Task.Delay(Change * 8); // smaller amounts should repeat faster
                Value -= Change;
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(DecreaseClickedEvent));
            }
        }
    }
    #endregion

    private void ResetText(TextBox tb)
    {
        tb.Text = 0 < Minimum ? Minimum.ToString() : "0";
        tb.SelectAll();
    }

    /// <summary>The Value property represents the TextBoxValue of the control.</summary>
    /// <returns>The current TextBoxValue of the control</returns>      
    public int Change
    {
        get { return (int)GetValue(ChangeProperty); }
        set { SetValue(ChangeProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Value.  This enables animation, styling, binding, etc.
    public static readonly DependencyProperty ChangeProperty =
        DependencyProperty.Register("Change", typeof(int), typeof(UpDownControl), new UIPropertyMetadata(10));

    public int Value
    {
        get { return (int)GetValue(ValueProperty); }
        set { tbValue.Text = value.ToString(); SetValue(ValueProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Value.  This enables animation, styling, binding, etc.
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(int), typeof(UpDownControl),
          new PropertyMetadata(0, new PropertyChangedCallback(OnSomeValuePropertyChanged)));

    /// <summary>
    /// Callback for ValueProperty
    /// </summary>
    private static void OnSomeValuePropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        UpDownControl? numericBox = target as UpDownControl;
        if (numericBox != null)
            numericBox.tbValue.Text = e.NewValue.ToString();
    }

    /// <summary>
    /// Maximum value for the Numeric Up Down control
    /// </summary>
    public int Maximum
    {
        get { return (int)GetValue(MaximumProperty); }
        set { SetValue(MaximumProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Maximum.  This enables animation, styling, binding, etc.
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register("Maximum", typeof(int), typeof(UpDownControl), new UIPropertyMetadata(100));

    /// <summary>
    /// Minimum value of the numeric up down conrol.
    /// </summary>
    public int Minimum
    {
        get { return (int)GetValue(MinimumProperty); }
        set { SetValue(MinimumProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Minimum.  This enables animation, styling, binding, etc.
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register("Minimum", typeof(int), typeof(UpDownControl), new UIPropertyMetadata(0));


    // Value changed
    private static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent("ValueChanged", RoutingStrategy.Bubble,
        typeof(RoutedEventHandler), typeof(UpDownControl));

    /// <summary>The ValueChanged event is called when the TextBoxValue of the control changes.</summary>
    public event RoutedEventHandler ValueChanged
    {
        add { AddHandler(ValueChangedEvent, value); }
        remove { RemoveHandler(ValueChangedEvent, value); }
    }

    // Increase button clicked
    private static readonly RoutedEvent IncreaseClickedEvent =
        EventManager.RegisterRoutedEvent("IncreaseClicked", RoutingStrategy.Bubble,
        typeof(RoutedEventHandler), typeof(UpDownControl));

    /// <summary>
    /// The IncreaseClicked event is called when the Increase button clicked
    /// </summary>
    public event RoutedEventHandler IncreaseClicked
    {
        add { AddHandler(IncreaseClickedEvent, value); }
        remove { RemoveHandler(IncreaseClickedEvent, value); }
    }

    // Decrease button clicked
    private static readonly RoutedEvent DecreaseClickedEvent =
        EventManager.RegisterRoutedEvent("DecreaseClicked", RoutingStrategy.Bubble,
        typeof(RoutedEventHandler), typeof(UpDownControl));

    /// <summary>
    /// The DecreaseClicked event is called when the Decrease button clicked
    /// </summary>
    public event RoutedEventHandler DecreaseClicked
    {
        add { AddHandler(DecreaseClickedEvent, value); }
        remove { RemoveHandler(DecreaseClickedEvent, value); }
    }

}
