using System;

namespace YellowInside.Models;

public sealed record ArcaconLoginPageNavigationArguments(
    Type ReturnPageType,
    object ReturnPageParameter,
    Type CancellationReturnPageType = null,
    object CancellationReturnPageParameter = null,
    bool UseBackStackOnLoginSuccess = false);
