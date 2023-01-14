using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lzw.Demonstration.Windows;
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        viewModel.EncodedMessageSegments.CollectionChanged += EncodedMessageSegmentsChanged;
        viewModel.DecodedMessageSegments.CollectionChanged += DecodedMessageSegmentsChanged;
    }

    void EncodedMessageSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs args) {
        Dispatcher.BeginInvoke(() => {
            encodedMessageTextBox.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            encodedMessageTextBox.Document.Blocks.Add(paragraph);
            Run run;
            var colored = false;
            
            foreach (var segment in viewModel.EncodedMessageSegments) {
                run = new() {
                    Text = segment,
                    Foreground = colored ? Brushes.Red : Brushes.Black
                };
                paragraph.Inlines.Add(run);
                colored = !colored;
            }
        }, DispatcherPriority.DataBind);
    }
    void DecodedMessageSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs args) {
        Dispatcher.BeginInvoke(() => {
            decodedMessageTextBox.Document.Blocks.Clear();
            var paragraph = new Paragraph();
            decodedMessageTextBox.Document.Blocks.Add(paragraph);
            Run run;
            var colored = false;
            
            foreach (var segment in viewModel.DecodedMessageSegments) {
                run = new() {
                    Text = segment,
                    Foreground = colored ? Brushes.Red : Brushes.Black
                };
                paragraph.Inlines.Add(run);
                colored = !colored;
            }
        }, DispatcherPriority.DataBind);
    }
}
