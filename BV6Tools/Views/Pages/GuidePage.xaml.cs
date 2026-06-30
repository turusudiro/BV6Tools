using System.Windows.Controls;

namespace BV6Tools.Views.Pages
{
    /// <summary>
    /// Interaction logic for GuidePage.xaml
    /// </summary>
    public partial class GuidePage : Page
    {
        public int GreenLumaLimit { get; }
        public GuidePage()
        {
            GreenLumaLimit = GreenLumaCommon.GreenLuma.Limit;
            DataContext = this;
            InitializeComponent();
        }
    }
}
