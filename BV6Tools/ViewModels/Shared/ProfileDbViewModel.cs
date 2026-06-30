using BV6Tools.Collections;

namespace BV6Tools.ViewModels.Shared
{
    public partial class ProfileDbViewModel : ObservableObject, IKeyed<int>
    {
        [ObservableProperty]
        public partial int ProfileID { get; set; }
        [ObservableProperty]
        public partial string ProfileName { get; set; } = string.Empty;

        public int Key => ProfileID;
    }
}
