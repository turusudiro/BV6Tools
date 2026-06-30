using Microsoft.Toolkit.Uwp.Notifications;

namespace BV6Tools.Services
{
    public interface IToastService
    {
        void Show(Action<ToastContentBuilder> configure, string tag, int durationSeconds = 3);
        Task ClearAll(bool immediately = false);
    }

    public class ToastService : IToastService
    {
        private readonly Dictionary<string, DateTime> _pendingTags = [];

        public void Show(Action<ToastContentBuilder> configure, string tag, int durationSeconds = 3)
        {
            var builder = new ToastContentBuilder();
            configure(builder);
            builder.Show(toast =>
            {
                toast.Tag = tag;
                toast.ExpirationTime = DateTime.Now.AddSeconds(durationSeconds);
            });

            _pendingTags[tag] = DateTime.Now.AddSeconds(durationSeconds);
            _ = Task.Delay(durationSeconds * 1000).ContinueWith(_ =>
            {
                ToastNotificationManagerCompat.History.Remove(tag);
                _pendingTags.Remove(tag);
            });
        }

        public async Task ClearAll(bool immediately = false)
        {
            if (!_pendingTags.Any()) return;

            if (!immediately)
            {
                var maxExpiry = _pendingTags.Values.Max();
                var remaining = (int)(maxExpiry - DateTime.Now).TotalMilliseconds;
                if (remaining > 0) await Task.Delay(remaining);
            }

            foreach (var (tag, expiry) in _pendingTags)
                if (immediately || DateTime.Now < expiry)
                    ToastNotificationManagerCompat.History.Remove(tag);

            _pendingTags.Clear();
        }
    }
}