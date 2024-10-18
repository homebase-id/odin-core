using System;

namespace Odin.Services.Configuration.VersionUpgrade;

public class VersionUpgradeRunningException(string message) : Exception(message);