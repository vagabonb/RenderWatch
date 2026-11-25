using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RenderWatch.App.ViewModels;

namespace RenderWatch.App.Views;

public partial class DisplayConstructorView : UserControl
{
    private readonly int[] _customColors = Enumerable.Repeat(0xFFFFFF, 16).ToArray();

    public DisplayConstructorView()
    {
        InitializeComponent();
    }

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TaskbarMetricAppearance metric, Tag: string field } ||
            Window.GetWindow(this) is not Window owner)
            return;

        var selectedBrush = field switch
        {
            nameof(metric.LabelColor) => metric.LabelBrush,
            nameof(metric.TemperatureColor) => metric.TemperatureBrush,
            _ => metric.ValueBrush
        };
        var color = (selectedBrush as SolidColorBrush)?.Color ??
            (owner.DataContext is MainViewModel { IsTaskbarStripLightMode: true } ? Colors.Black : Colors.White);
        IntPtr buffer = Marshal.AllocHGlobal(_customColors.Length * sizeof(int));
        try
        {
            Marshal.Copy(_customColors, 0, buffer, _customColors.Length);
            var dialog = new ColorDialogData
            {
                Size = Marshal.SizeOf<ColorDialogData>(),
                Owner = new WindowInteropHelper(owner).Handle,
                Rgb = (uint)(color.R | color.G << 8 | color.B << 16),
                CustomColors = buffer,
                Flags = 0x00000103 // RGBINIT, FULLOPEN, ANYCOLOR
            };
            if (!ChooseColor(ref dialog)) return;
            Marshal.Copy(buffer, _customColors, 0, _customColors.Length);
            string hex = $"#{dialog.Rgb & 255:X2}{(dialog.Rgb >> 8) & 255:X2}{(dialog.Rgb >> 16) & 255:X2}";
            switch (field)
            {
                case nameof(metric.LabelColor): metric.LabelColor = hex; break;
                case nameof(metric.ValueColor): metric.ValueColor = hex; break;
                case nameof(metric.TemperatureColor): metric.TemperatureColor = hex; break;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ColorDialogData dialog);

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorDialogData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public uint Rgb;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CallerData;
        public IntPtr Hook;
        public IntPtr Template;
    }
}
