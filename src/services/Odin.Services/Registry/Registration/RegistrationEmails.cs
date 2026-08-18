//
// SEB:NOTE
// These are temporary place holders and should probably be
// exchanged with something a bit more professional looking...
//

using System.Collections.Generic;
using System.Linq;
using Odin.Core.Dns;

namespace Odin.Services.Registry.Registration;

public static class RegistrationEmails
{
    //
    // Provisioning Completed
    //
    public static string ProvisioningCompletedText(string email, string domain, string link,
        IReadOnlyCollection<DsRecordData> dnssecDsRecords = null)
    {
        return @$"
            Hi {email},

            Your new {domain} identity is ready.

            Please click here {link} to go to it!
{DnssecTextSection(domain, dnssecDsRecords)}
            --
            Team Homebase
        ";
    }

    // Only rendered when the domain is one DS record away from a fully validated DNSSEC
    // chain (status DsMissing); every other state gets no DNSSEC mention
    private static string DnssecTextSection(string domain, IReadOnlyCollection<DsRecordData> dsRecords)
    {
        if (dsRecords == null || dsRecords.Count == 0)
        {
            return "";
        }

        var records = string.Join("\n", dsRecords.Select(ds =>
            $"              Key tag: {ds.KeyTag}   Algorithm: {ds.Algorithm}   Digest type: {ds.DigestType}\n" +
            $"              Digest: {ds.Digest}"));

        return @$"
            Optional: protect {domain} with DNSSEC.
            Your DNS zone is already cryptographically signed. To complete the chain of trust,
            add this DS record where your domain is delegated: at your domain registrar if
            {domain} is a registered (apex) domain, or as a DS record next to your NS records
            at your DNS host if it is a subdomain:

{records}
";
    }

    public static string ProvisioningCompletedHtml(string domain, string link,
        IReadOnlyCollection<DsRecordData> dnssecDsRecords = null)
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
                                                            <p style='margin-bottom: 15px'>Hi there,</p>
                                                            <p style='margin-bottom: 15px'>
                                                                Your new <a href='{link}' style='text-decoration:underlin;'>{domain}</a> identity is ready.<br/><br/>
                                                                Click <a href='{link}' style='text-decoration:underlin;'>here</a>  to go to it!<br/><br/>
                                                                Please note that due to DNS propagation, your new identity may take up to 48 hours to become accessible across the globe.
                                                            </p>
{DnssecHtmlSection(domain, dnssecDsRecords)}
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

    //

    private static string DnssecHtmlSection(string domain, IReadOnlyCollection<DsRecordData> dsRecords)
    {
        if (dsRecords == null || dsRecords.Count == 0)
        {
            return "";
        }

        var rows = string.Join("", dsRecords.Select(ds =>
            $"<tr>" +
            $"<td style='padding:4px 12px 4px 0'>{ds.KeyTag}</td>" +
            $"<td style='padding:4px 12px 4px 0'>{ds.Algorithm}</td>" +
            $"<td style='padding:4px 12px 4px 0'>{ds.DigestType}</td>" +
            $"<td style='padding:4px 0; word-break:break-all'><code>{ds.Digest}</code></td>" +
            $"</tr>"));

        return @$"
                                                            <p style='margin-bottom: 15px'>
                                                                <strong>Optional: protect {domain} with DNSSEC.</strong><br/>
                                                                Your DNS zone is already cryptographically signed. To complete the chain of trust,
                                                                add this DS record where your domain is delegated: at your domain registrar if
                                                                {domain} is a registered (apex) domain, or as a DS record next to your NS records
                                                                at your DNS host if it is a subdomain.
                                                            </p>
                                                            <table style='font-size:13px; border-collapse:collapse; margin-bottom:15px'>
                                                                <tr>
                                                                    <th style='text-align:left; padding:4px 12px 4px 0'>Key tag</th>
                                                                    <th style='text-align:left; padding:4px 12px 4px 0'>Algorithm</th>
                                                                    <th style='text-align:left; padding:4px 12px 4px 0'>Digest type</th>
                                                                    <th style='text-align:left; padding:4px 0'>Digest</th>
                                                                </tr>
                                                                {rows}
                                                            </table>
";
    }
}
