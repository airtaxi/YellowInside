using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YellowInside.Controls;

public sealed partial class CardControl : UserControl
{
    public static readonly DependencyProperty ThumbnailSourceProperty =
        DependencyProperty.Register(
            nameof(ThumbnailSource),
            typeof(ImageSource),
            typeof(CardControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(CardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(CardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TagsProperty =
        DependencyProperty.Register(
            nameof(Tags),
            typeof(ObservableCollection<string>),
            typeof(CardControl),
            new PropertyMetadata(null));

    public CardControl()
    {
        InitializeComponent();
    }

    public ImageSource ThumbnailSource
    {
        get => (ImageSource)GetValue(ThumbnailSourceProperty);
        set => SetValue(ThumbnailSourceProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ObservableCollection<string> Tags
    {
        get => (ObservableCollection<string>)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }
}
