using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lzw.Demonstration.Controls;
public partial class PhraseBitsTableView : UserControl {
    public static readonly DependencyProperty ItemsSourceProperty;
    public static readonly DependencyProperty InnerBorderPenProperty;
    public static readonly DependencyProperty EntryHeightProperty;
    public static readonly DependencyProperty IsReversedProperty;
    public static readonly DependencyProperty MarkedPairCountProperty;
    public static readonly DependencyProperty MarkingBrushProperty;

    public IEnumerable<PhraseBitsPair>? ItemsSource {
        get => GetValue(ItemsSourceProperty) as IEnumerable<PhraseBitsPair>;
        set => SetValue(ItemsSourceProperty, value);
    }
    public Pen? InnerBorderPen {
        get => GetValue(InnerBorderPenProperty) as Pen;
        set => SetValue(InnerBorderPenProperty, value);
    }
    public double EntryHeight {
        get => (double)GetValue(EntryHeightProperty);
        set => SetValue(EntryHeightProperty, value);
    }
    public bool IsReversed {
        get => (bool)GetValue(IsReversedProperty);
        set => SetValue(IsReversedProperty, value);
    }
    public int MarkedPairCount {
        get => (int)GetValue(MarkedPairCountProperty);
        set => SetValue(MarkedPairCountProperty, value);
    }
    public Brush? MarkingBrush {
        get => GetValue(MarkingBrushProperty) as Brush;
        set => SetValue(MarkingBrushProperty, value);
    }

    static PhraseBitsTableView() {
        ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<PhraseBitsPair>),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (sender, args) => {
                    var @this = (PhraseBitsTableView)sender;

                    if (args.OldValue is INotifyCollectionChanged oldValueAsObservable)
                        oldValueAsObservable.CollectionChanged -= @this.OnItemsSourceChanged;
                    if (args.NewValue is INotifyCollectionChanged newValueAsObservable)
                        newValueAsObservable.CollectionChanged += @this.OnItemsSourceChanged;
                }));
        InnerBorderPenProperty = DependencyProperty.Register(
            nameof(InnerBorderPen),
            typeof(Pen),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        EntryHeightProperty = DependencyProperty.Register(
            nameof(EntryHeight),
            typeof(double),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(30D, FrameworkPropertyMetadataOptions.AffectsRender));
        IsReversedProperty = DependencyProperty.Register(
            nameof(IsReversed),
            typeof(bool),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        MarkedPairCountProperty = DependencyProperty.Register(
            nameof(MarkedPairCount),
            typeof(int),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender),
            value => (int)value >= 0);
        MarkingBrushProperty = DependencyProperty.Register(
            nameof(MarkingBrush),
            typeof(Brush),
            typeof(PhraseBitsTableView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    }
    public PhraseBitsTableView() {
        InitializeComponent();
    }

    void OnItemsSourceChanged(object? sender, NotifyCollectionChangedEventArgs args) {
        Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.DataBind);
    }
    protected override void OnRender(DrawingContext context) {
        context.DrawRectangle(Background, null, new(default, RenderSize));
        
        if (InnerBorderPen is not null) context.DrawLine(
            InnerBorderPen,
            new(ActualWidth / 2, 0),
            new(ActualWidth / 2, ActualHeight));
        
        DrawEntry(context, FormatText("Фраза"), FormatText("Биты"));

        if (ItemsSource is null || !ItemsSource.GetEnumerator().MoveNext()) return;

        var y = EntryHeight;

        foreach(var pair in ItemsSource.Take(ItemsSource.Count() - MarkedPairCount)) {
            DrawPair(context, pair, y);

            y += EntryHeight;
        }
        foreach(var pair in ItemsSource.Skip(ItemsSource.Count() - MarkedPairCount)) {
            DrawPair(context, pair, y, true);

            y += EntryHeight;
        }
    }
    void DrawPair(DrawingContext context, PhraseBitsPair pair, double y = 0, bool marked = false) {
        var builder = new StringBuilder();

        foreach (var bit in pair.Bits.Span)
            builder.Append(bit ? '1' : '0');

        var formattedPhrase = FormatText(pair.Phrase.ToString(), marked);
        var formattedBits = FormatText(builder.ToString(), marked);

        DrawEntry(context, formattedPhrase, formattedBits, y);
    }
    void DrawEntry(DrawingContext context, FormattedText formattedPhrase, FormattedText formattedBits, double y = 0) {
        if (ActualWidth <= 20) return;
        if (IsReversed) {
            var temporary = formattedPhrase;
            formattedPhrase = formattedBits;
            formattedBits = temporary;
        }

        formattedPhrase.MaxTextWidth = ActualWidth / 2 - 10;
        formattedBits.MaxTextWidth = ActualWidth / 2 - 10;
        context.DrawText(formattedPhrase, new(5, y + (EntryHeight - formattedPhrase.Height) / 2));
        context.DrawText(formattedBits, new(ActualWidth / 2 + 5, y + (EntryHeight - formattedBits.Height) / 2));

        if (InnerBorderPen is not null)
            context.DrawLine(InnerBorderPen, new(0, y + EntryHeight), new(ActualWidth, y + EntryHeight));
    }
#pragma warning disable CS0618
    FormattedText FormatText(string text, bool marked = false) => new(
        text,
        CultureInfo.CurrentCulture,
        GetFlowDirection(this),
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
        FontSize,
        marked ? MarkingBrush ?? Foreground : Foreground);
#pragma warning restore CS0618
}
