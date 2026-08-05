using CommunityToolkit.Mvvm.ComponentModel;
using R3;

namespace ScrollViewerBringIntoViewSample.ViewModels;

public class ValueViewModel : ObservableObject
{
    public ValueViewModel(decimal value)
    {
        this.Current = new BindableReactiveProperty<decimal>(value);
    }

    public BindableReactiveProperty<decimal> Current { get; init; }
}
