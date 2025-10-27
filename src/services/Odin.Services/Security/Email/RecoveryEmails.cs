//
// SEB:NOTE
// These are temporary place holders and should probably be
// exchanged with something a bit more professional looking...
//

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Odin.Core.Identity;
using Odin.Services.Security.Health.RiskAnalyzer;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Email;

public static class RecoveryEmails
{
    //
    // Enter recovery mode
    //
    public static string EnterRecoveryModeVerifyEmailText(string email, string domain, string link, List<ShamiraPlayer> players)
    {
        return @$"
            Hi {email},

            You or someone else has requested to put your identity ({domain}) into recovery mode.

            If you did not do this, delete this email.

            The following connections have a portion of your recovery key.  They will receive a notification when you verify using 
            the link below: {string.Join(",", players.Select(p => $"{p.OdinId} ({p.Type})"))}
                To continue putting your identity into recovery mode open this link in your browser: {
                    link
                }

        --
            Team Homebase
        ";
    }

    public static string EnterRecoveryModeVerifyEmailHtml(string domain, string link, List<ShamiraPlayer> players)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<ul>");
        foreach (var player in players)
        {
            builder.Append("<li>");
            builder.Append($"{player.OdinId} ({player.Type})");
            builder.Append("</li>");
        }

        builder.AppendLine("</ul>");
        var playersList = builder.ToString();

        return @$"
            <!DOCTYPE html>
            <html>
                <head>
                    <meta name='viewport' content='width=device-width' />
                    <meta http-equiv='Content-Type' content='text/html; charset=UTF-8' />
                    <title>Homebase</title>
                    <!--[if mso]><style type='text/css'>h1,h2,h3,h4,p,ul,ol,table td,body {{font-family: sans-serif, Arial, Helvetica !important;}}</style>[endif]-->
                    <style>
                        @media only screen and (max-width: 620px) {{
                            table[class='body'] h1 {{
                                font-size: 28px !important;
                                margin-bottom: 10px !important;
                            }}
                            table[class='body'] a,
                            table[class='body'] ol,
                            table[class='body'] p,
                            table[class='body'] span,
                            table[class='body'] td,
                            table[class='body'] ul {{
                                font-size: 16px !important;
                            }}
                            table[class='body'] .article,
                            table[class='body'] .wrapper {{
                                padding: 10px !important;
                            }}
                            table[class='body'] .content {{
                                padding: 0 !important;
                            }}
                            table[class='body'] .container {{
                                padding: 0 !important;
                                width: 100% !important;
                            }}
                            table[class='body'] .main {{
                                border-left-width: 0 !important;
                                border-radius: 0 !important;
                                border-right-width: 0 !important;
                            }}
                            table[class='body'] .btn table {{
                                width: 100% !important;
                            }}
                            table[class='body'] .btn a {{
                                width: 100% !important;
                            }}
                            table[class='body'] .img-responsive {{
                                height: auto !important;
                                max-width: 100% !important;
                                width: auto !important;
                            }}
                        }}
                    </style>
                </head>
                <body
                    style='
                        background-color: #f6f6f6;
                        font-family: sans-serif, Arial, Helvetica !important;
                        -webkit-font-smoothing: antialiased;
                        font-size: 14px;
                        line-height: 1.4;
                        margin: 0;
                        padding: 0;
                        -ms-text-size-adjust: 100%;
                        -webkit-text-size-adjust: 100%;
                    '
                >
                    <table
                        border='0'
                        cellpadding='0'
                        cellspacing='0'
                        class='body'
                        style='
                            border-collapse: separate;
                            mso-table-lspace: 0;
                            mso-table-rspace: 0;
                            background-color: #f6f6f6;
                            width: 100%;
                        '
                        width='100%'
                        bgcolor='#f6f6f6'
                    >
                        <tr>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                            <td
                                class='container'
                                style='
                                    font-size: 14px;
                                    vertical-align: top;
                                    display: block;
                                    max-width: 580px;
                                    padding: 10px;
                                    width: 580px;
                                    margin: 0 auto;
                                '
                                width='580'
                                valign='top'
                            >
                                <div
                                    class='content'
                                    style='
                                        box-sizing: border-box;
                                        display: block;
                                        margin: 0 auto;
                                        max-width: 580px;
                                        padding: 10px;
                                    '
                                >
                                    <table
                                        class='main'
                                        style='
                                            border-collapse: separate;
                                            mso-table-lspace: 0;
                                            mso-table-rspace: 0;
                                            background: #fff;
                                            border-radius: 3px;
                                            width: 100%;
                                        '
                                        width='100%'
                                    >
                                        <tr>
                                            <td
                                                class='wrapper'
                                                style='
                                                    font-size: 14px;
                                                    vertical-align: top;
                                                    box-sizing: border-box;
                                                    padding: 20px;
                                                '
                                                valign='top'
                                            >
                                                <table
                                                    border='0'
                                                    cellpadding='0'
                                                    cellspacing='0'
                                                    style='
                                                        border-collapse: separate;
                                                        mso-table-lspace: 0;
                                                        mso-table-rspace: 0;
                                                        width: 100%;
                                                    '
                                                    width='100%'
                                                >
                                                    <tr>
                                                        <td
                                                            style='
                                                                font-size: 14px;
                                                                vertical-align: top;
                                                                font-weight: 400;
                                                                margin: 0;
                                                            '
                                                            valign='top'
                                                        >
                                                            <p style='margin-bottom: 15px'>Hi there,</p>
                                                            <p style='margin-bottom: 15px'>
                                                                You or someone else has requested to put your identity ({domain}) into recovery mode.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                If you did not do this, delete this email.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                The following connections have a portion of your recovery key.  They will receive a notification when you verify using 
                                                                the link below.
                                                            </p>
                                                            <p style='margin-bottom: 15px'>
                                                                {playersList}
                                                            </p>
                                                            <p style='margin-bottom: 15px'>
                                                                To continue putting your identity into recovery mode, click <a href='{link}' style='text-decoration:underline;'>here</a>.
                                                            </p>

                                                            <p style='margin-bottom: 15px'>
                                                                Kind regards<br />Team Homebase
                                                            </p>
                                                        </td>
                                                    </tr>
                                                </table>
                                            </td>
                                        </tr>
                                    </table>
                                    <div
                                        class='footer'
                                        style='clear: both; margin-top: 10px; text-align: center; width: 100%'
                                    >
                                        <table
                                            border='0'
                                            cellpadding='0'
                                            cellspacing='0'
                                            style='
                                                border-collapse: separate;
                                                mso-table-lspace: 0;
                                                mso-table-rspace: 0;
                                                width: 100%;
                                            '
                                            width='100%'
                                        >
                                            <tr>
                                                <td
                                                    class='content-block powered-by'
                                                    style='
                                                        vertical-align: top;
                                                        padding-bottom: 10px;
                                                        padding-top: 10px;
                                                        color: #999;
                                                        font-size: 12px;
                                                        text-align: center;
                                                    '
                                                    valign='top'
                                                    align='center'
                                                >
                                                    <a
                                                        href='https://homebase.id'
                                                        style='
                                                            color: #999;
                                                            font-size: 12px;
                                                            text-align: center;
                                                            text-decoration: none;
                                                        '
                                                        >Homebase</a
                                                    >
                                                </td>
                                            </tr>
                                        </table>
                                    </div>
                                </div>
                            </td>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                        </tr>
                    </table>
                </body>
            </html>"
            ;
    }

    // Exit recovery mode

    public static string ExitRecoveryModeEmailText(string email, string domain, string link)
    {
        return @$"
            Hi {email},

            You or someone else has requested to exit recovery mode of your identity ({domain}).

            If you did not do this, delete this email.

            To exit recovery mode open this link in your browser: {link}

            --
            Team Homebase
        ";
    }

    public static string ExitRecoveryModeEmailHtml(string domain, string link)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto;'>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>Hi there,</p>");
        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine($"        You or someone else has requested to exit recovery mode for your identity <strong>{domain}</strong>.");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        If you did not make this request, you can safely ignore and delete this email.");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        To confirm and exit recovery mode, please click the button below:");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='text-align: center; margin: 30px 0;'>");
        sb.AppendLine($"        <a href='{link}' style='display: inline-block; background-color: #2563eb; color: #fff; " +
                      "padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold;'>");
        sb.AppendLine("            Exit Recovery Mode");
        sb.AppendLine("        </a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        If the button above does not work, copy and paste the following link into your browser:<br />");
        sb.AppendLine($"        <a href='{link}' style='color: #2563eb; word-break: break-all;'>{link}</a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-top: 30px; border-top: 1px solid #ddd; padding-top: 15px; color: #555; font-size: 14px;'>");
        sb.AppendLine("        Kind regards,<br />");
        sb.AppendLine("        Team Homebase");
        sb.AppendLine("    </p>");

        sb.AppendLine("</div>");

        return Template(sb.ToString());
    }

    // finalize recovery using recovery key

    public static string FinalizeRecoveryUsingRecoveryKeyText(string domain, string link)
    {
        return @$"
            Hi {domain},

            We have assembled your recovery key.

            To recover your identity, open this link in your browser: {link}

            --
            Team Homebase
        ";
    }

    public static string FinalizeRecoveryUsingRecoveryKeyHtml(string domain, string link)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto;'>");

        sb.AppendLine($"    <p style='margin-bottom: 15px;'>Hi {domain},</p>");
        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        We have generated your recovery key to help you regain access to your account.");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        To complete the recovery process, please click the button below:");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='text-align: center; margin: 30px 0;'>");
        sb.AppendLine($"        <a href='{link}' style='display: inline-block; background-color: #2563eb; color: #fff; " +
                      "padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold;'>");
        sb.AppendLine("            Finalize Recovery");
        sb.AppendLine("        </a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        If the button above does not work, copy and paste the following link into your browser:<br />");
        sb.AppendLine($"        <a href='{link}' style='color: #2563eb; word-break: break-all;'>{link}</a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-top: 30px; border-top: 1px solid #ddd; padding-top: 15px; color: #555; font-size: 14px;'>");
        sb.AppendLine("        Kind regards,<br />");
        sb.AppendLine("        Team Homebase");
        sb.AppendLine("    </p>");

        sb.AppendLine("</div>");

        return Template(sb.ToString());
    }

    public static string FormatRecoveryRiskStatusText(OdinId odinId, RecoveryInfo info)
    {
        var risk = info.RecoveryRisk;
        var tenant = odinId;

        if (!info.IsConfigured)
        {
            return @$"
Hi {tenant},

⚠️ Account Recovery is not yet configured.

Your account currently has no recovery setup. This means if you lose access, you will not be able to recover your account.

Please set up Account Recovery by adding trusted connections as soon as possible:

👉 https://{tenant}/owner/security/password-recovery

--
Team Homebase
".Trim();
        }

        var validCount = risk.ValidShardCount;
        var minRequired = risk.MinRequired;
        var recoverableText = risk.IsRecoverable
            ? "✅ You currently have enough shards to recover your account."
            : "❌ You do not currently have enough shards to recover your account.";

        var headline = risk.RiskLevel switch
        {
            RecoveryRiskLevel.Low => "✅ Your Account Recovery is safe",
            RecoveryRiskLevel.Moderate => "⚠️ Account Recovery is fragile — add at least one more trusted connection",
            RecoveryRiskLevel.High => "🚨 Account Recovery at risk — add at least two more trusted connections",
            RecoveryRiskLevel.Critical => "💀 Account Recovery not possible — immediate action required",
            _ => "ℹ️ Account Recovery status unknown"
        };

        return @$"
Hi {tenant},

{headline}

Recovery details:
- Usable shards: {validCount}
- Minimum required: {minRequired}
- Recoverable: {(risk.IsRecoverable ? "Yes" : "No")}
- Risk level: {risk.RiskLevel}

{recoverableText}

We recommend checking your recovery contacts and ensuring that all listed players are trusted and available.

You can manage your Account Recovery here:
👉 https://{tenant}/owner/security/password-recovery

--
Team Homebase
".Trim();
    }

    public static string FormatRecoveryRiskStatusHtml(OdinId odinId, RecoveryInfo info)
    {
        var risk = info.RecoveryRisk;
        var tenant = odinId;

        if (!info.IsConfigured)
        {
            return Template($@"
    <h2 style='margin-bottom: 15px;'>⚠️ Account Recovery not configured</h2>

    <p style='margin-bottom: 15px;'>
        Your account <strong>{tenant}</strong> does not currently have Account Recovery configured.
    </p>

    <p style='margin-bottom: 15px;'>
        <strong>Warning:</strong> If you lose access, you will not be able to recover your account.
    </p>

    <p style='margin-bottom: 20px;'>
        Please set up Account Recovery by adding trusted connections as soon as possible.
    </p>

    <p style='margin-bottom: 30px;'>
        <a href='https://{tenant}/owner/security/password-recovery' style='color: #0052cc; text-decoration: none; font-weight: 600;'>Open Account Recovery Settings →</a>
    </p>

    <p style='margin-top: 30px; border-top: 1px solid #ddd; padding-top: 15px; color: #555; font-size: 14px;'>
        Kind regards,<br />Team Homebase
    </p>
");
        }

        var validCount = risk.ValidShardCount;
        var minRequired = risk.MinRequired;
        var recoverableText = risk.IsRecoverable
            ? "✅ You currently have enough shards to recover your account."
            : "❌ You do not currently have enough shards to recover your account.";

        var headline = risk.RiskLevel switch
        {
            RecoveryRiskLevel.Low => "✅ Your Account Recovery is safe",
            RecoveryRiskLevel.Moderate => "⚠️ Account Recovery is fragile — add at least one more trusted connection",
            RecoveryRiskLevel.High => "🚨 Account Recovery at risk — add at least two more trusted connections",
            RecoveryRiskLevel.Critical => "💀 Account Recovery not possible — immediate action required",
            _ => "ℹ️ Account Recovery status unknown"
        };

        return Template($@"
    <h2 style='margin-bottom: 15px;'>{headline}</h2>

    <p style='margin-bottom: 15px;'>
        Account Recovery status for your account <strong>{tenant}</strong>:
    </p>

    <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px;'>
        <tr>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>Usable shards</td>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>{validCount}</td>
        </tr>
        <tr>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>Minimum required</td>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>{minRequired}</td>
        </tr>
        <tr>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>Recoverable</td>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>{(risk.IsRecoverable ? "Yes ✅" : "No ❌")}</td>
        </tr>
        <tr>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>Risk level</td>
            <td style='padding: 8px; border-bottom: 1px solid #ddd;'>{risk.RiskLevel}</td>
        </tr>
    </table>

    <p style='margin-bottom: 15px;'>
        {recoverableText}
    </p>

    <p style='margin-top: 25px;'>
        We recommend reviewing your Account Recovery configuration and ensuring that all your trusted connections are active and secure.
    </p>

    <p style='margin-top: 25px;'>
        <a href='https://{tenant}/owner/security/password-recovery' style='color: #0052cc; text-decoration: none; font-weight: 600;'>Manage Account Recovery →</a>
    </p>

    <p style='margin-top: 30px; border-top: 1px solid #ddd; padding-top: 15px; color: #555; font-size: 14px;'>
        Kind regards,<br />Team Homebase
    </p>
");
    }

    public static string VerifyNewRecoveryEmailText(string domain, string link)
    {
        return @$"
            Hi {domain},

            You or someone else has changed the email address used when recovering your account.

            You need to verify you have access to this email by opening this link in your browser: {link}

            This will complete the verification process.
            --
            Team Homebase
        ";
    }

    public static string VerifyNewRecoveryEmailHtml(string domain, string link)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<div style='font-family: Arial, sans-serif; color: #333; line-height: 1.6; max-width: 600px; margin: 0 auto;'>");

        sb.AppendLine($"    <p style='margin-bottom: 15px;'>Hi {domain},</p>");
        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        You or someone else has changed the email address used for recovering your account.");
        sb.AppendLine("    </p>");
        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        To verify that you have access to this email, please click the button below:");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='text-align: center; margin: 30px 0;'>");
        sb.AppendLine($"        <a href='{link}' style='display: inline-block; background-color: #2563eb; color: #fff; " +
                      "padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold;'>");
        sb.AppendLine("            Verify Email");
        sb.AppendLine("        </a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-bottom: 15px;'>");
        sb.AppendLine("        If the button above does not work, copy and paste the following link into your browser:<br />");
        sb.AppendLine($"        <a href='{link}' style='color: #2563eb; word-break: break-all;'>{link}</a>");
        sb.AppendLine("    </p>");

        sb.AppendLine("    <p style='margin-top: 30px; border-top: 1px solid #ddd; padding-top: 15px; color: #555; font-size: 14px;'>");
        sb.AppendLine("        Kind regards,<br />");
        sb.AppendLine("        Team Homebase");
        sb.AppendLine("    </p>");

        sb.AppendLine("</div>");

        return Template(sb.ToString());
    }

    private static string Template(string contents)
    {
        return @$"
            <!DOCTYPE html>
            <html>
                <head>
                    <meta name='viewport' content='width=device-width' />
                    <meta http-equiv='Content-Type' content='text/html; charset=UTF-8' />
                    <title>Homebase</title>
                    <!--[if mso]><style type='text/css'>h1,h2,h3,h4,p,ul,ol,table td,body {{font-family: sans-serif, Arial, Helvetica !important;}}</style>[endif]-->
                    <style>
                        @media only screen and (max-width: 620px) {{
                            table[class='body'] h1 {{
                                font-size: 28px !important;
                                margin-bottom: 10px !important;
                            }}
                            table[class='body'] a,
                            table[class='body'] ol,
                            table[class='body'] p,
                            table[class='body'] span,
                            table[class='body'] td,
                            table[class='body'] ul {{
                                font-size: 16px !important;
                            }}
                            table[class='body'] .article,
                            table[class='body'] .wrapper {{
                                padding: 10px !important;
                            }}
                            table[class='body'] .content {{
                                padding: 0 !important;
                            }}
                            table[class='body'] .container {{
                                padding: 0 !important;
                                width: 100% !important;
                            }}
                            table[class='body'] .main {{
                                border-left-width: 0 !important;
                                border-radius: 0 !important;
                                border-right-width: 0 !important;
                            }}
                            table[class='body'] .btn table {{
                                width: 100% !important;
                            }}
                            table[class='body'] .btn a {{
                                width: 100% !important;
                            }}
                            table[class='body'] .img-responsive {{
                                height: auto !important;
                                max-width: 100% !important;
                                width: auto !important;
                            }}
                        }}
                    </style>
                </head>
                <body
                    style='
                        background-color: #f6f6f6;
                        font-family: sans-serif, Arial, Helvetica !important;
                        -webkit-font-smoothing: antialiased;
                        font-size: 14px;
                        line-height: 1.4;
                        margin: 0;
                        padding: 0;
                        -ms-text-size-adjust: 100%;
                        -webkit-text-size-adjust: 100%;
                    '
                >
                    <table
                        border='0'
                        cellpadding='0'
                        cellspacing='0'
                        class='body'
                        style='
                            border-collapse: separate;
                            mso-table-lspace: 0;
                            mso-table-rspace: 0;
                            background-color: #f6f6f6;
                            width: 100%;
                        '
                        width='100%'
                        bgcolor='#f6f6f6'
                    >
                        <tr>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                            <td
                                class='container'
                                style='
                                    font-size: 14px;
                                    vertical-align: top;
                                    display: block;
                                    max-width: 580px;
                                    padding: 10px;
                                    width: 580px;
                                    margin: 0 auto;
                                '
                                width='580'
                                valign='top'
                            >
                                <div
                                    class='content'
                                    style='
                                        box-sizing: border-box;
                                        display: block;
                                        margin: 0 auto;
                                        max-width: 580px;
                                        padding: 10px;
                                    '
                                >
                                    <table
                                        class='main'
                                        style='
                                            border-collapse: separate;
                                            mso-table-lspace: 0;
                                            mso-table-rspace: 0;
                                            background: #fff;
                                            border-radius: 3px;
                                            width: 100%;
                                        '
                                        width='100%'
                                    >
                                        <tr>
                                            <td
                                                class='wrapper'
                                                style='
                                                    font-size: 14px;
                                                    vertical-align: top;
                                                    box-sizing: border-box;
                                                    padding: 20px;
                                                '
                                                valign='top'
                                            >
                                                <table
                                                    border='0'
                                                    cellpadding='0'
                                                    cellspacing='0'
                                                    style='
                                                        border-collapse: separate;
                                                        mso-table-lspace: 0;
                                                        mso-table-rspace: 0;
                                                        width: 100%;
                                                    '
                                                    width='100%'
                                                >
                                                    <tr>
                                                        <td
                                                            style='
                                                                font-size: 14px;
                                                                vertical-align: top;
                                                                font-weight: 400;
                                                                margin: 0;
                                                            '
                                                            valign='top'
                                                        >
                                                        {contents}
                                                        </td>
                                                    </tr>
                                                </table>
                                            </td>
                                        </tr>
                                    </table>
                                    <div
                                        class='footer'
                                        style='clear: both; margin-top: 10px; text-align: center; width: 100%'
                                    >
                                        <table
                                            border='0'
                                            cellpadding='0'
                                            cellspacing='0'
                                            style='
                                                border-collapse: separate;
                                                mso-table-lspace: 0;
                                                mso-table-rspace: 0;
                                                width: 100%;
                                            '
                                            width='100%'
                                        >
                                            <tr>
                                                <td
                                                    class='content-block powered-by'
                                                    style='
                                                        vertical-align: top;
                                                        padding-bottom: 10px;
                                                        padding-top: 10px;
                                                        color: #999;
                                                        font-size: 12px;
                                                        text-align: center;
                                                    '
                                                    valign='top'
                                                    align='center'
                                                >
                                                    <a
                                                        href='https://homebase.id'
                                                        style='
                                                            color: #999;
                                                            font-size: 12px;
                                                            text-align: center;
                                                            text-decoration: none;
                                                        '
                                                        >Homebase</a
                                                    >
                                                </td>
                                            </tr>
                                        </table>
                                    </div>
                                </div>
                            </td>
                            <td style='font-size: 14px; vertical-align: top' valign='top'>&nbsp;</td>
                        </tr>
                    </table>
                </body>
            </html>"
            ;
    }
}