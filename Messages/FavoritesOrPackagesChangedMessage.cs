using CommunityToolkit.Mvvm.Messaging.Messages;
using YellowInside.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace YellowInside.Messages;

public class FavoritesOrPackagesChangedMessage(ContentSource contentSource, int packageIndex) : ValueChangedMessage<int>(packageIndex)
{
    public ContentSource Source { get; } = contentSource;
}
