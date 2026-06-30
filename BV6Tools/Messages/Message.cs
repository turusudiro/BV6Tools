using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Shared;
using CommunityToolkit.Mvvm.Messaging.Messages;
using STCommon;

namespace BV6Tools.Messages
{
    public sealed record LuaAddedMessage(LuaData LuaData, uint AppId, string? Name);
    public sealed record SaveMessage;
    public sealed record NavigationPageBadgeMessage(string NavigationPageName, int Count);

    public static class MessengerTokens
    {
        public const string Dashboard = "Dashboard";
        public const string GameService = "GameService";
        public const string List = "List";
    }

    public sealed record AddedMessage(uint AppID, string? Name = default, IEnumerable<uint>? DLC = null, bool? IsEnabled = null);

    public sealed record DownloadMessage(uint AppId, string? Name);

    public class CounterVisibleChangedMessage(bool value) : ValueChangedMessage<bool>(value);

    public class InjectModeChangedMessage(ProcessMode processMode) : ValueChangedMessage<ProcessMode>(processMode);

    public class ProfileChangedMessage(ProfileDbViewModel profile) : ValueChangedMessage<ProfileDbViewModel>(profile);
}