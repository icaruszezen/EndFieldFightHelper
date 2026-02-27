using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using EndFieldFightHelper.Helpers;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class OverlayWindow : Window
{
    private enum DragMode { None, Move, ResizeRight, ResizeBottom, ResizeBottomRight }

    private DragMode _dragMode;
    private Point _dragStart;
    private PixelPoint _windowStartPos;
    private PixelPoint _moveMouseOffset;
    private double _startWidth;
    private double _startHeight;

    private const double EdgeThreshold = 8;
    private const double OverlayMinWidth = 100;
    private const double OverlayMinHeight = 60;

    private Win32Helper.SUBCLASSPROC? _subclassProc;

    public OverlayWindow()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
        DataContextChanged += OnDataContextChanged;
    }

    private OverlayViewModel? Vm => DataContext as OverlayViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (Vm != null)
        {
            Vm.LogMessages.CollectionChanged += OnLogMessagesChanged;
        }
    }

    private void OnLogMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LogScrollViewer?.ScrollToEnd();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InstallHitTestHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveHitTestHook();
        base.OnClosed(e);
    }

    private void InstallHitTestHook()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        _subclassProc = HitTestSubclassProc;
        Win32Helper.SetWindowSubclass(handle, _subclassProc, UIntPtr.Zero, IntPtr.Zero);
    }

    private void RemoveHitTestHook()
    {
        if (_subclassProc == null) return;

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        Win32Helper.RemoveWindowSubclass(handle, _subclassProc, UIntPtr.Zero);
        _subclassProc = null;
    }

    private IntPtr HitTestSubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == Win32Helper.WM_NCHITTEST)
        {
            int screenX = (short)(lParam.ToInt64() & 0xFFFF);
            int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            var pt = new Win32Helper.POINT { X = screenX, Y = screenY };
            Win32Helper.ScreenToClient(hWnd, ref pt);

            double scale = VisualRoot?.RenderScaling ?? 1.0;
            double logicalX = pt.X / scale;
            double logicalY = pt.Y / scale;

            if (Vm is { IsEditMode: true })
            {
                return Win32Helper.DefSubclassProc(hWnd, msg, wParam, lParam);
            }

            if (EditToggleButton != null)
            {
                var topLeft = EditToggleButton.TranslatePoint(new Point(0, 0), this);
                if (topLeft.HasValue)
                {
                    var btnW = EditToggleButton.Bounds.Width;
                    var btnH = EditToggleButton.Bounds.Height;
                    if (logicalX >= topLeft.Value.X && logicalX <= topLeft.Value.X + btnW &&
                        logicalY >= topLeft.Value.Y && logicalY <= topLeft.Value.Y + btnH)
                    {
                        return new IntPtr(Win32Helper.HTCLIENT);
                    }
                }
            }

            return new IntPtr(Win32Helper.HTTRANSPARENT);
        }

        return Win32Helper.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        PositionResizeGrips();
    }

    private void PositionResizeGrips()
    {
        if (GripRight == null || GripBottom == null || GripBottomRight == null) return;

        var w = Bounds.Width;
        var h = Bounds.Height;

        Canvas.SetLeft(GripRight, w - 6);
        Canvas.SetTop(GripRight, 0);
        GripRight.Height = h;

        Canvas.SetLeft(GripBottom, 0);
        Canvas.SetTop(GripBottom, h - 6);
        GripBottom.Width = w;

        Canvas.SetLeft(GripBottomRight, w - 10);
        Canvas.SetTop(GripBottomRight, h - 10);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Vm is not { IsEditMode: true }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(this);
        var w = Bounds.Width;
        var h = Bounds.Height;

        bool nearRight = pos.X >= w - EdgeThreshold;
        bool nearBottom = pos.Y >= h - EdgeThreshold;

        if (nearRight && nearBottom)
            _dragMode = DragMode.ResizeBottomRight;
        else if (nearRight)
            _dragMode = DragMode.ResizeRight;
        else if (nearBottom)
            _dragMode = DragMode.ResizeBottom;
        else
            _dragMode = DragMode.Move;

        _dragStart = pos;
        _windowStartPos = Position;
        _startWidth = Width;
        _startHeight = Height;

        if (_dragMode == DragMode.Move)
        {
            var screenPt = this.PointToScreen(pos);
            _moveMouseOffset = new PixelPoint(screenPt.X - Position.X, screenPt.Y - Position.Y);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragMode == DragMode.None) return;

        var current = e.GetPosition(this);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;

        switch (_dragMode)
        {
            case DragMode.Move:
                var screenNow = this.PointToScreen(current);
                Position = new PixelPoint(
                    screenNow.X - _moveMouseOffset.X,
                    screenNow.Y - _moveMouseOffset.Y);
                break;

            case DragMode.ResizeRight:
                Width = Math.Max(OverlayMinWidth, _startWidth + dx);
                break;

            case DragMode.ResizeBottom:
                Height = Math.Max(OverlayMinHeight, _startHeight + dy);
                break;

            case DragMode.ResizeBottomRight:
                Width = Math.Max(OverlayMinWidth, _startWidth + dx);
                Height = Math.Max(OverlayMinHeight, _startHeight + dy);
                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragMode == DragMode.None) return;

        e.Pointer.Capture(null);
        _dragMode = DragMode.None;

        Vm?.NotifyPositionSizeChanged(Position.X, Position.Y, Width, Height);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragMode == DragMode.None)
            Cursor = new Cursor(StandardCursorType.Arrow);
    }
}
