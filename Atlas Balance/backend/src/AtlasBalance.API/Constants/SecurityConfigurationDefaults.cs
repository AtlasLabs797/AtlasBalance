namespace AtlasBalance.API.Constants;

public static class SecurityConfigurationDefaults
{
    public const string MfaRememberDeviceEnabledKey = "mfa_remember_device_enabled";
    public const string MfaRequireForNonAdminUsersKey = "require_mfa_for_non_admin_users";
    public const int MfaRememberDeviceDays = 90;
}
