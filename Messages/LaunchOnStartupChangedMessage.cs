using CommunityToolkit.Mvvm.Messaging.Messages;

namespace YellowInside.Messages;

public class LaunchOnStartupChangedMessage(bool isEnabled) : ValueChangedMessage<bool>(isEnabled);
