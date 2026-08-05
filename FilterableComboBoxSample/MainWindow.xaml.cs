using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FilterableComboBoxSample.ViewModels;

namespace FilterableComboBoxSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private bool _scrollBarInitialized = false;

    private void OnPrefectureSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectionText.Text = PrefectureComboBox.SelectedItem is string prefecture
            ? $"選択中: {prefecture}"
            : "まだ選択されていません。";
    }

    private void UpdateVerticalScrollBar()
    {
        var contentHeight = this.ValuesItemsControl.ActualHeight;
        var viewportHeight = this.ValuesScrollViewer.ActualHeight;

        this.VerticalScrollBar.ViewportSize = viewportHeight;
        this.VerticalScrollBar.Maximum = Math.Max(0, contentHeight - viewportHeight);
    }

    private void Window_LayoutUpdated(object sender, EventArgs e)
    {
        if (!this._scrollBarInitialized)
        {
            Debug.WriteLine($"LayoutUpdated: ValuesItemsControl.ActualHeight={this.ValuesItemsControl.ActualHeight}, ValuesScrollViewer.ActualHeight={this.ValuesScrollViewer.ActualHeight}");

            this.ValuesScrollViewer.UpdateLayout();
            UpdateVerticalScrollBar();

            this._scrollBarInitialized = true;
        }
    }

    private void VerticalScrollBar_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        this.ValuesScrollViewer.ScrollToVerticalOffset(e.NewValue);
    }

    private void ValuesScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVerticalScrollBar();
    }

    private void ValuesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        this.VerticalScrollBar.Value = this.ValuesScrollViewer.VerticalOffset;
    }
}
