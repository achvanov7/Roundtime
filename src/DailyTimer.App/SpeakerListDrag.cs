using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DailyTimer.App
{
    internal sealed class SpeakerListDrag<T> where T : class
    {
        private readonly ListBox list;
        private readonly SpeakerDragFeedback feedback;
        private readonly Func<T, Guid> idOf;
        private readonly Func<T, string> nameOf;
        private readonly Predicate<T> canMove;
        private readonly Action<T, T> move;
        private readonly string dataFormat = typeof(T).FullName;
        private T candidate;
        private Point dragStart;

        public SpeakerListDrag(ListBox list, SpeakerDragFeedback feedback, Func<T, Guid> idOf, Func<T, string> nameOf,
            Predicate<T> canMove, Action<T, T> move)
        {
            this.list = list;
            this.feedback = feedback;
            this.idOf = idOf;
            this.nameOf = nameOf;
            this.canMove = canMove;
            this.move = move;
            list.PreviewMouseLeftButtonDown += MouseDown;
            list.PreviewMouseMove += MouseMove;
            list.PreviewDragOver += DragOver;
            list.PreviewDragLeave += delegate { feedback.Hide(); };
            list.PreviewDrop += Drop;
        }

        private void MouseDown(object sender, MouseButtonEventArgs e)
        {
            candidate = null;
            var grip = e.OriginalSource as FrameworkElement;
            var row = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (row == null) return;
            row.IsSelected = true;
            var entry = row.DataContext as T;
            if (grip == null || grip.Name != "SpeakerDragHandle" || entry == null || !canMove(entry)) return;
            candidate = entry;
            dragStart = e.GetPosition(list);
        }

        private void MouseMove(object sender, MouseEventArgs e)
        {
            if (candidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(list);
            if (Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance &&
                Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            var id = idOf(candidate);
            candidate = null;
            try { DragDrop.DoDragDrop(list, new DataObject(dataFormat, id), DragDropEffects.Move); }
            finally { feedback.Hide(); }
        }

        private void DragOver(object sender, DragEventArgs e)
        {
            var source = GetSource(e);
            if (source == null) { feedback.Hide(); return; }
            var target = GetTarget(e, source);
            e.Handled = true;
            e.Effects = target == null ? DragDropEffects.None : DragDropEffects.Move;
            bool after = target != null && list.Items.IndexOf(source) < list.Items.IndexOf(target.DataContext);
            feedback.Show(nameOf(source), e, list, target, after);
        }

        private void Drop(object sender, DragEventArgs e)
        {
            feedback.Hide();
            var source = GetSource(e);
            if (source == null) return;
            var target = GetTarget(e, source);
            e.Handled = true;
            e.Effects = target == null ? DragDropEffects.None : DragDropEffects.Move;
            if (target != null) move(source, (T)target.DataContext);
        }

        private T GetSource(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(dataFormat)) return null;
            var id = e.Data.GetData(dataFormat);
            return id is Guid ? list.Items.OfType<T>().FirstOrDefault(entry => idOf(entry) == (Guid)id && canMove(entry)) : null;
        }

        private ListBoxItem GetTarget(DragEventArgs e, T source)
        {
            var origin = e.OriginalSource as DependencyObject;
            var target = Ancestor<ListBoxItem>(origin);
            if (target == null)
            {
                if (list.Items.Count == 0 || Ancestor<ScrollBar>(origin) != null) return null;
                target = list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) as ListBoxItem;
                if (target == null || e.GetPosition(target).Y < target.ActualHeight) return null;
            }
            var entry = target.DataContext as T;
            return entry != null && entry != source && canMove(entry) ? target : null;
        }

        private static TElement Ancestor<TElement>(DependencyObject node) where TElement : DependencyObject
        {
            while (node != null)
            {
                var match = node as TElement;
                if (match != null) return match;
                var content = node as FrameworkContentElement;
                node = content != null ? content.Parent : VisualTreeHelper.GetParent(node);
            }
            return null;
        }
    }

    internal sealed class SpeakerDragFeedback
    {
        private readonly Canvas overlay;
        private readonly Border preview;
        private readonly TextBlock previewName;
        private readonly Border marker;

        public SpeakerDragFeedback(Canvas overlay, Border preview, TextBlock previewName, Border marker)
        {
            this.overlay = overlay;
            this.preview = preview;
            this.previewName = previewName;
            this.marker = marker;
        }

        public void Show(string name, DragEventArgs e, ListBox list, ListBoxItem target, bool after)
        {
            previewName.Text = name;
            preview.Visibility = Visibility.Visible;
            preview.Measure(new Size(overlay.ActualWidth, overlay.ActualHeight));
            Point pointer = e.GetPosition(overlay);
            const double offset = 14;
            double left = pointer.X + offset;
            double top = pointer.Y + offset;
            if (left + preview.DesiredSize.Width > overlay.ActualWidth) left = pointer.X - offset - preview.DesiredSize.Width;
            if (top + preview.DesiredSize.Height > overlay.ActualHeight) top = pointer.Y - offset - preview.DesiredSize.Height;
            Canvas.SetLeft(preview, Math.Max(0, left));
            Canvas.SetTop(preview, Math.Max(0, top));

            marker.Visibility = target == null ? Visibility.Collapsed : Visibility.Visible;
            if (target == null) return;
            Point edge = target.TranslatePoint(new Point(), overlay);
            if (after) edge.Y += target.ActualHeight;
            Point listTop = list.TranslatePoint(new Point(), overlay);
            marker.Width = target.ActualWidth;
            Canvas.SetLeft(marker, edge.X);
            Canvas.SetTop(marker, Math.Max(listTop.Y, Math.Min(edge.Y - marker.Height / 2, listTop.Y + list.ActualHeight - marker.Height)));
        }

        public void Hide()
        {
            preview.Visibility = Visibility.Collapsed;
            marker.Visibility = Visibility.Collapsed;
        }
    }
}
