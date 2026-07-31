using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace FilterableComboBoxSample.Controls;

/// <summary>
/// An editable ComboBox that filters its items as the user types.
/// </summary>
public class FilterableComboBox : ComboBox
{
    private static readonly DependencyPropertyKey FilteredItemCountPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(FilteredItemCount),
            typeof(int),
            typeof(FilterableComboBox),
            new PropertyMetadata(0));

    public static readonly DependencyProperty FilteredItemCountProperty =
        FilteredItemCountPropertyKey.DependencyProperty;

    public static readonly DependencyProperty FilterMemberPathProperty =
        DependencyProperty.Register(
            nameof(FilterMemberPath),
            typeof(string),
            typeof(FilterableComboBox),
            new PropertyMetadata(string.Empty, OnFilterPropertyChanged));

    public static readonly DependencyProperty IsCaseSensitiveProperty =
        DependencyProperty.Register(
            nameof(IsCaseSensitive),
            typeof(bool),
            typeof(FilterableComboBox),
            new PropertyMetadata(false, OnFilterPropertyChanged));

    private TextBox? _editableTextBox;
    private ICollectionView? _itemsView;
    private Predicate<object>? _originalFilter;
    private Predicate<object>? _combinedFilter;
    private string _searchText = string.Empty;
    private bool _isUpdating;
    private bool _isCommittingSelection;
    private object? _pendingItem;
    private ComboBoxItem? _highlightedContainer;
    private object _previousBackground = DependencyProperty.UnsetValue;
    private object _previousForeground = DependencyProperty.UnsetValue;
    private BindingBase? _previousBackgroundBinding;
    private BindingBase? _previousForegroundBinding;

    static FilterableComboBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FilterableComboBox),
            new FrameworkPropertyMetadata(typeof(ComboBox)));
    }

    public FilterableComboBox()
    {
        IsEditable = true;
        IsTextSearchEnabled = false;
        StaysOpenOnEdit = true;
        IsSynchronizedWithCurrentItem = false;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Gets or sets the property path used for filtering. When empty,
    /// DisplayMemberPath and then ToString() are used.
    /// </summary>
    public string FilterMemberPath
    {
        get => (string)GetValue(FilterMemberPathProperty);
        set => SetValue(FilterMemberPathProperty, value);
    }

    public bool IsCaseSensitive
    {
        get => (bool)GetValue(IsCaseSensitiveProperty);
        set => SetValue(IsCaseSensitiveProperty, value);
    }

    public int FilteredItemCount
    {
        get => (int)GetValue(FilteredItemCountProperty);
        private set => SetValue(FilteredItemCountPropertyKey, value);
    }

    public override void OnApplyTemplate()
    {
        if (_editableTextBox is not null)
        {
            _editableTextBox.TextChanged -= OnEditableTextBoxTextChanged;
        }

        base.OnApplyTemplate();

        _editableTextBox = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (_editableTextBox is not null)
        {
            _editableTextBox.TextChanged += OnEditableTextBoxTextChanged;
        }
    }

    protected override void OnItemsSourceChanged(
        System.Collections.IEnumerable oldValue,
        System.Collections.IEnumerable newValue)
    {
        DetachView();
        base.OnItemsSourceChanged(oldValue, newValue);

        if (IsLoaded)
        {
            AttachView();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            (!string.IsNullOrEmpty(_searchText) ||
             _pendingItem is not null ||
             SelectedItem is not null))
        {
            ClearPendingHighlight();
            _isUpdating = true;
            SelectedItem = null;
            Text = string.Empty;
            if (_editableTextBox is not null)
            {
                _editableTextBox.Text = string.Empty;
            }

            _isUpdating = false;
            _searchText = string.Empty;
            ApplyFilter(openDropDown: false);
            IsDropDownOpen = false;
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Up || e.Key == Key.Down) &&
            SelectAdjacentFilteredItem(e.Key == Key.Down))
        {
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Enter || e.Key == Key.Tab) && CommitCurrentCandidate())
        {
            IsDropDownOpen = false;

            // Tab must remain unhandled so that focus moves to the next control.
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        ClearPendingHighlight();
        base.OnDropDownClosed(e);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);

        if (_isCommittingSelection)
        {
            return;
        }

        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not object selectedItem)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            ClearPendingHighlight();
            CommitItem(selectedItem);
        }, DispatcherPriority.DataBind);
    }

    private static void OnFilterPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((FilterableComboBox)dependencyObject).ApplyFilter();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachView();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachView();
    }

    private void AttachView()
    {
        if (_itemsView is not null)
        {
            return;
        }

        _itemsView = ItemsSource is null
            ? CollectionViewSource.GetDefaultView(Items)
            : CollectionViewSource.GetDefaultView(ItemsSource);

        _originalFilter = _itemsView.Filter;
        _combinedFilter = item =>
            (_originalFilter?.Invoke(item) ?? true) && MatchesSearch(item);
        _itemsView.Filter = _combinedFilter;

        if (_itemsView is INotifyCollectionChanged observableView)
        {
            observableView.CollectionChanged += OnViewCollectionChanged;
        }

        ApplyFilter(openDropDown: false);
    }

    private void DetachView()
    {
        if (_itemsView is null)
        {
            return;
        }

        if (_itemsView is INotifyCollectionChanged observableView)
        {
            observableView.CollectionChanged -= OnViewCollectionChanged;
        }

        if (ReferenceEquals(_itemsView.Filter, _combinedFilter))
        {
            _itemsView.Filter = _originalFilter;
        }

        _itemsView = null;
        _originalFilter = null;
        _combinedFilter = null;
    }

    private void OnEditableTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating || _editableTextBox is null)
        {
            return;
        }

        _searchText = _editableTextBox.Text;
        ClearPendingHighlight();
        ApplyFilter();
    }

    private bool SelectAdjacentFilteredItem(bool moveNext)
    {
        if (_itemsView is null)
        {
            return false;
        }

        var candidates = _itemsView.Cast<object>().ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var currentIndex = _pendingItem is null
            ? -1
            : candidates.IndexOf(_pendingItem);

        var nextIndex = moveNext
            ? Math.Min(currentIndex + 1, candidates.Count - 1)
            : currentIndex < 0
                ? candidates.Count - 1
                : Math.Max(currentIndex - 1, 0);

        _pendingItem = candidates[nextIndex];
        IsDropDownOpen = true;

        Dispatcher.BeginInvoke(
            HighlightPendingItem,
            DispatcherPriority.Loaded);
        return true;
    }

    private bool CommitCurrentCandidate()
    {
        if (_itemsView is null)
        {
            return false;
        }

        // A selected item with no active search or keyboard highlight is already
        // committed. Enter and Tab must not replace it with the first item.
        if (_pendingItem is null &&
            string.IsNullOrEmpty(_searchText) &&
            SelectedItem is not null)
        {
            return false;
        }

        var candidate = _pendingItem is not null && _itemsView.Contains(_pendingItem)
            ? _pendingItem
            : _itemsView.Cast<object>().FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        _isCommittingSelection = true;
        SelectedItem = candidate;
        _isCommittingSelection = false;
        ClearPendingHighlight();
        CommitItem(candidate);
        return true;
    }

    private void CommitItem(object item)
    {
        _searchText = string.Empty;
        ApplyFilter(openDropDown: false);
        RestoreEditorText(GetFilterText(item));
    }

    private void RestoreEditorText(string text)
    {
        _isUpdating = true;
        Text = text;

        if (_editableTextBox is not null)
        {
            _editableTextBox.Text = text;
            _editableTextBox.CaretIndex = text.Length;
            _editableTextBox.SelectionLength = 0;
        }

        _isUpdating = false;
    }

    private void HighlightPendingItem()
    {
        RestoreHighlightedContainer();

        if (_pendingItem is null)
        {
            return;
        }

        _highlightedContainer =
            ItemContainerGenerator.ContainerFromItem(_pendingItem) as ComboBoxItem;

        if (_highlightedContainer is null)
        {
            return;
        }

        _previousBackground =
            _highlightedContainer.ReadLocalValue(BackgroundProperty);
        _previousForeground =
            _highlightedContainer.ReadLocalValue(ForegroundProperty);
        _previousBackgroundBinding =
            BindingOperations.GetBindingBase(_highlightedContainer, BackgroundProperty);
        _previousForegroundBinding =
            BindingOperations.GetBindingBase(_highlightedContainer, ForegroundProperty);

        _highlightedContainer.SetResourceReference(
            BackgroundProperty,
            SystemColors.HighlightBrushKey);
        _highlightedContainer.SetResourceReference(
            ForegroundProperty,
            SystemColors.HighlightTextBrushKey);
        _highlightedContainer.BringIntoView();
    }

    private void ClearPendingHighlight()
    {
        _pendingItem = null;
        RestoreHighlightedContainer();
    }

    private void RestoreHighlightedContainer()
    {
        if (_highlightedContainer is null)
        {
            return;
        }

        RestorePropertyValue(
            _highlightedContainer,
            BackgroundProperty,
            _previousBackground,
            _previousBackgroundBinding);
        RestorePropertyValue(
            _highlightedContainer,
            ForegroundProperty,
            _previousForeground,
            _previousForegroundBinding);

        _highlightedContainer = null;
        _previousBackground = DependencyProperty.UnsetValue;
        _previousForeground = DependencyProperty.UnsetValue;
        _previousBackgroundBinding = null;
        _previousForegroundBinding = null;
    }

    private static void RestorePropertyValue(
        DependencyObject target,
        DependencyProperty property,
        object previousValue,
        BindingBase? previousBinding)
    {
        if (previousBinding is not null)
        {
            BindingOperations.SetBinding(target, property, previousBinding);
        }
        else if (previousValue == DependencyProperty.UnsetValue)
        {
            target.ClearValue(property);
        }
        else
        {
            target.SetValue(property, previousValue);
        }
    }

    private void ApplyFilter(bool openDropDown = true)
    {
        if (_itemsView is null || _isUpdating)
        {
            return;
        }

        _isUpdating = true;
        var caretIndex = _editableTextBox?.CaretIndex ?? 0;

        _itemsView.Refresh();
        FilteredItemCount = _itemsView.Cast<object>().Count();

        if (_editableTextBox is not null)
        {
            _editableTextBox.Text = _searchText;
            _editableTextBox.CaretIndex = Math.Min(caretIndex, _searchText.Length);
            _editableTextBox.SelectionLength = 0;

            if (openDropDown && _editableTextBox.IsKeyboardFocusWithin)
            {
                IsDropDownOpen = true;
            }
        }

        _isUpdating = false;
    }

    private bool MatchesSearch(object item)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        var itemText = GetFilterText(item);
        var options = CompareOptions.IgnoreNonSpace;
        if (!IsCaseSensitive)
        {
            options |= CompareOptions.IgnoreCase;
        }

        return _searchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                itemText,
                term,
                options) >= 0);
    }

    private string GetFilterText(object item)
    {
        var propertyPath = string.IsNullOrWhiteSpace(FilterMemberPath)
            ? DisplayMemberPath
            : FilterMemberPath;

        object? value = item;
        foreach (var memberName in propertyPath.Split(
                     '.',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (value is null)
            {
                break;
            }

            value = TypeDescriptor.GetProperties(value)[memberName]?.GetValue(value);
        }

        return value?.ToString() ?? string.Empty;
    }

    private void OnViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FilteredItemCount = _itemsView?.Cast<object>().Count() ?? 0;
    }
}
