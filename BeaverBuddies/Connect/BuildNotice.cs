using Timberborn.CoreUI;
using Timberborn.Localization;

namespace BeaverBuddies.Connect
{
    /**
     * Tells the host who it turned away for another build (see
     * ServerEventIO): a player on an older build doesn't know why its
     * connection closed, so the host says it.
     */
    public class BuildNotice : RegisteredSingleton
    {
        private readonly DialogBoxShower _dialogBoxShower;
        private readonly ILoc _loc;

        public BuildNotice(DialogBoxShower dialogBoxShower, ILoc loc)
        {
            _dialogBoxShower = dialogBoxShower;
            _loc = loc;
        }

        public static void Show(string name, string theirs) => SingletonManager.GetSingleton<BuildNotice>()?.ShowNotice(name, theirs);

        private void ShowNotice(string name, string theirs)
        {
            _dialogBoxShower.Create()
                .SetMessage(_loc.T("BeaverBuddies.Host.RefusedBuild", name, theirs, BuildIdentity.Describe))
                .Show();
        }
    }
}
