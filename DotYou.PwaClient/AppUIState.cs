using System;

namespace DotYou.PwaClient
{
    public class AppUIState
    {
        private bool _bottomTabsVisible;

        public bool BottomTabsVisible
        {
            get => _bottomTabsVisible;
            set
            {
                _bottomTabsVisible = value;
                this.NotifyStateChanged();
            }
        }

        public event Action OnChange;

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}